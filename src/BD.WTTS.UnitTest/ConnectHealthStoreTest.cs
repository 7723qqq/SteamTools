using System.Collections;
using System.Reflection;

namespace BD.WTTS.UnitTest;

/// <summary>
/// 连接健康度排序测试
/// </summary>
/// <remarks>
/// 代理连接时按历史连接结果决定先试哪个 IP：成功且快的优先，未知次之，
/// 失败或记录过期的靠后。排序只允许影响顺序，不允许丢地址。
/// </remarks>
public sealed class ConnectHealthStoreTest
{
    static readonly IPAddress fast = IPAddress.Parse("10.0.0.1");
    static readonly IPAddress slow = IPAddress.Parse("10.0.0.2");
    static readonly IPAddress other = IPAddress.Parse("10.0.0.3");

    [Test]
    public void OrderKeepsOriginalWhenNoHistory()
    {
        var store = new ConnectHealthStore();

        var result = store.Order("github.com", new[] { other, slow, fast });

        Assert.That(result, Is.EqualTo(new[] { other, slow, fast }));
    }

    [Test]
    public void OrderPutsFasterAddressFirst()
    {
        var store = new ConnectHealthStore();
        store.Record("github.com", slow, TimeSpan.FromMilliseconds(180));
        store.Record("github.com", fast, TimeSpan.FromMilliseconds(20));

        var result = store.Order("github.com", new[] { slow, other, fast });

        // 规则：已知可用按实测耗时升序，未知随后，失败最后
        Assert.That(result[0], Is.EqualTo(fast));
        Assert.That(result[1], Is.EqualTo(slow));
        Assert.That(result[2], Is.EqualTo(other));
        Assert.That(result.Count, Is.EqualTo(3), "排序不能丢地址");
    }

    [Test]
    public void OrderPutsFailedAddressLast()
    {
        var store = new ConnectHealthStore();
        store.Record("github.com", fast, null);

        var result = store.Order("github.com", new[] { fast, other });

        Assert.That(result[0], Is.EqualTo(other));
        Assert.That(result[1], Is.EqualTo(fast));
    }

    [Test]
    public void OrderIgnoresExpiredEntries()
    {
        var store = new ConnectHealthStore(TimeSpan.Zero);
        store.Record("github.com", slow, TimeSpan.FromMilliseconds(10));
        store.Record("github.com", fast, TimeSpan.FromMilliseconds(500));

        var result = store.Order("github.com", new[] { slow, fast });

        Assert.That(result, Is.EqualTo(new[] { slow, fast }), "记录过期后应回到原始顺序");
    }

    [Test]
    public void OrderRemovesExpiredEntries()
    {
        var store = new ConnectHealthStore(TimeSpan.Zero);
        store.Record("github.com", fast, TimeSpan.FromMilliseconds(10));

        store.Order("github.com", new[] { fast });

        var field = typeof(ConnectHealthStore).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entries = (IDictionary)field.GetValue(store)!;
        Assert.That(entries.Count, Is.Zero);
    }

    [Test]
    public void OrderIsScopedByHost()
    {
        var store = new ConnectHealthStore();
        store.Record("a.example", fast, TimeSpan.FromMilliseconds(1));

        var result = store.Order("b.example", new[] { slow, fast });

        Assert.That(result, Is.EqualTo(new[] { slow, fast }));
    }

    [Test]
    public void OrderKeepsSingleAddress()
    {
        var store = new ConnectHealthStore();
        store.Record("github.com", fast, null);

        var result = store.Order("github.com", new[] { fast });

        Assert.That(result, Is.EqualTo(new[] { fast }));
    }
}
