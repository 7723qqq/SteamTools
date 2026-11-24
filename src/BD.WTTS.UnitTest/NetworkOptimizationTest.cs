using System.Reflection;
using BD.WTTS.Models;
using BD.WTTS.Services;
using BD.WTTS.Services.Implementation;
using Moq;

namespace BD.WTTS.UnitTest;

public sealed class NetworkOptimizationTest
{
    static readonly IPAddress fixedAddress = IPAddress.Parse("192.0.2.1");
    static readonly IPAddress forwardAddress = IPAddress.Parse("192.0.2.2");
    static readonly IPAddress originAddress = IPAddress.Parse("192.0.2.3");

    [Test]
    public void CertificateRenewsOnlyWithinThirtyDays()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Multiple(() =>
        {
            Assert.That(CertificateRenewalPolicy.ShouldRenew(now, now.AddDays(29)), Is.True);
            Assert.That(CertificateRenewalPolicy.ShouldRenew(now, now.AddDays(31)), Is.False);
        });
    }

    [Test]
    public void ProxyTokenExpiresBeforeActualExpiryAndIsScopedToUser()
    {
        var type = typeof(ProxyService).GetNestedType("ProxyTokenCache", BindingFlags.NonPublic)!;
        var constructor = type.GetConstructor(new[]
        {
            typeof(string), typeof(DateTimeOffset), typeof(Guid),
        })!;
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var token = constructor.Invoke(new object[] { "token", now.AddMinutes(10), userId });
        var isValid = type.GetMethod("IsValid")!;

        Assert.Multiple(() =>
        {
            Assert.That(isValid.Invoke(token, new object[] { userId, now }), Is.True);
            Assert.That(isValid.Invoke(token, new object[] { Guid.NewGuid(), now }), Is.False);
            Assert.That(isValid.Invoke(token, new object[] { userId, now.AddMinutes(6) }), Is.False);
        });
    }

    [Test]
    public async Task ConfiguredIpCanBeExcludedFromFallbackCandidates()
    {
        var resolver = new Mock<IDomainResolver>();
        resolver.Setup(x => x.ResolveAsync(It.IsAny<DnsEndPoint>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAddresses);
        var config = new DomainConfig { IPAddress = fixedAddress };
        using var handler = new ReverseProxyHttpClientHandler(config, resolver.Object, HttpNoProxy.Instance);

        var candidates = await handler.GetIPEndPointsAsync(
            new DnsEndPoint("origin.test", 443), includeConfiguredIp: false, default).ToArrayAsync();

        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public async Task ForwardDestinationCandidatesUseTheirOwnHealthHost()
    {
        var resolver = new Mock<IDomainResolver>();
        resolver.Setup(x => x.ResolveAsync(
                It.Is<DnsEndPoint>(x => x.Host == "forward.test"), It.IsAny<CancellationToken>()))
            .Returns(Resolve(forwardAddress));
        resolver.Setup(x => x.ResolveAsync(
                It.Is<DnsEndPoint>(x => x.Host == "origin.test"), It.IsAny<CancellationToken>()))
            .Returns(Resolve(originAddress));
        var config = new DomainConfig { IPAddress = fixedAddress, ForwardDestination = "forward.test" };
        using var handler = new ReverseProxyHttpClientHandler(config, resolver.Object, HttpNoProxy.Instance);

        var candidates = await handler.GetIPEndPointsAsync(
            new DnsEndPoint("origin.test", 443), includeConfiguredIp: true, default).ToArrayAsync();

        Assert.Multiple(() =>
        {
            Assert.That(candidates.Select(x => x.HealthHost), Is.EqualTo(new[]
            {
                "origin.test", "forward.test", "origin.test",
            }));
            Assert.That(candidates.Select(x => x.EndPoint.Address), Is.EqualTo(new[]
            {
                fixedAddress, forwardAddress, originAddress,
            }));
        });
    }

    [Test]
    public void ScriptResponseBufferStreamsAfterLimit()
    {
        using var destination = new MemoryStream();
        using var stream = new ScriptResponseBufferStream(destination, 4);
        var payload = new byte[] { 1, 2, 3, 4, 5, 6 };

        stream.Write(payload);

        Assert.Multiple(() =>
        {
            Assert.That(stream.Passthrough, Is.True);
            Assert.That(stream.Buffer.Length, Is.Zero);
            Assert.That(destination.ToArray(), Is.EqualTo(payload));
        });
    }

    [Test]
    public void ConnectHealthCanRemoveOneHostOrAllHosts()
    {
        var store = new ConnectHealthStore();
        store.Record("a.test", fixedAddress, TimeSpan.FromMilliseconds(10));
        store.Record("b.test", forwardAddress, TimeSpan.FromMilliseconds(20));

        store.Remove("A.TEST");
        store.Order("a.test", new[] { fixedAddress, originAddress });
        store.Order("b.test", new[] { forwardAddress, originAddress });

        store.Clear();
        var field = typeof(ConnectHealthStore).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entries = (System.Collections.IDictionary)field.GetValue(store)!;
        Assert.That(entries.Count, Is.Zero);
    }

    static async IAsyncEnumerable<IPAddress> EmptyAddresses(
        DnsEndPoint endPoint, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    static async IAsyncEnumerable<IPAddress> Resolve(
        IPAddress address,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return address;
    }
}
