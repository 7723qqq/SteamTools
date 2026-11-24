// https://github.com/dotnetcore/FastGithub/blob/2.1.4/FastGithub.DomainResolve/DomainResolver.cs

//using AppResources = BD.WTTS.Client.Resources.Strings;

// ReSharper disable once CheckNamespace
using System.Net.NetworkInformation;

namespace BD.WTTS.Services.Implementation;

sealed class DomainResolver : IDomainResolver, IDisposable
{
    readonly IReverseProxyConfig reverseProxyConfig;
    readonly IPCToastService toast;

    readonly ConnectHealthStore health = new();
    readonly object networkSync = new();
    System.Threading.Timer? networkChangeTimer;
    int isIpv6;
    bool disposed;

    public DomainResolver(IPCToastService toast, IReverseProxyConfig reverseProxyConfig)
    {
        this.reverseProxyConfig = reverseProxyConfig;
        this.toast = toast;
        isIpv6 = reverseProxyConfig.Service.IsSupportIpv6 ? 1 : 0;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    public void RecordConnectResult(string host, IPAddress address, TimeSpan? elapsed)
        => health.Record(host, address, elapsed);

    public void InvalidateDns(string host)
    {
        health.Remove(host);
        reverseProxyConfig.Service.DnsAnalysis.InvalidateDns(host);
    }

    public async IAsyncEnumerable<IPAddress> ResolveAsync(DnsEndPoint endPoint, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var addresses = new List<IPAddress>();
        try
        {
            // Hosts and DNS interception must not resolve an accelerated domain back to this proxy.
            var servers = reverseProxyConfig.Service.ProxyMode is ProxyMode.DNSIntercept or ProxyMode.Hosts
                ? IDnsAnalysisService.DNS_Dnspods
                : null;
            await foreach (var ip in reverseProxyConfig.Service.DnsAnalysis
                .AnalysisDomainIpAsync(endPoint.Host, servers, Volatile.Read(ref isIpv6) != 0, cancellationToken)
                .ConfigureAwait(false))
            {
                addresses.Add(ip);
            }
        }
        catch (Exception ex)
        {
            toast.ShowAppend(
                IPCToastService.ToastIcon.Error,
                IPCToastService.ToastText.CommunityFix_DNSErrorNotify,
                Environment.NewLine + "ExceptionMessage: " + ex.Message);
            throw;
        }

        // 按历史连接结果优先尝试更快的地址
        foreach (var ip in health.Order(endPoint.Host, addresses))
            yield return ip;
    }

    public async void CheckIpv6SupportAsync(CancellationToken cancellationToken = default)
        => await RefreshIpv6SupportAsync();

    async Task RefreshIpv6SupportAsync()
    {
        Volatile.Write(ref isIpv6, reverseProxyConfig.Service.IsSupportIpv6 ? 1 : 0);
        var supported = await reverseProxyConfig.Service.DnsAnalysis.GetIsIpv6SupportAsync();
        if (!disposed)
            Volatile.Write(ref isIpv6, supported ? 1 : 0);
    }

    void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        lock (networkSync)
        {
            if (disposed) return;
            networkChangeTimer?.Dispose();
            networkChangeTimer = new System.Threading.Timer(
                _ => _ = Task.Run(HandleNetworkAddressChangedAsync),
                null,
                TimeSpan.FromMilliseconds(500),
                Timeout.InfiniteTimeSpan);
        }
    }

    async Task HandleNetworkAddressChangedAsync()
    {
        health.Clear();
        reverseProxyConfig.Service.DnsAnalysis.ClearDnsCache();
        await RefreshIpv6SupportAsync();
    }

    public Task TestSpeedAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        lock (networkSync)
        {
            if (disposed) return;
            disposed = true;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            networkChangeTimer?.Dispose();
            networkChangeTimer = null;
        }
        health.Clear();
    }
}
