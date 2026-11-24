namespace BD.WTTS.UnitTest;

/// <summary>
/// 线程池预热测试
/// </summary>
/// <remarks>
/// 运行时默认的最小线程数为处理器数量，突发并发时按每 500 毫秒一个的速率注入线程，
/// 会让大量异步回调排队。这里锁定预热后的下限，以及重复调用不会反向降低已有配置。
/// </remarks>
public sealed class ThreadPoolHelperTest
{
    static int ExpectedMinThreads => Math.Clamp(Environment.ProcessorCount * 2, 8, 64);

    [Test]
    public void ConfigureMinThreadsRaisesMinimumThreads()
    {
        ThreadPoolHelper.ConfigureMinThreads();

        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

        Assert.That(workerThreads, Is.GreaterThanOrEqualTo(ExpectedMinThreads));
        Assert.That(completionPortThreads, Is.GreaterThanOrEqualTo(ExpectedMinThreads));
    }

    [Test]
    public void ConfigureMinThreadsIsIdempotent()
    {
        ThreadPoolHelper.ConfigureMinThreads();
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

        ThreadPoolHelper.ConfigureMinThreads();
        ThreadPool.GetMinThreads(out var workerThreadsAfterSecondCall, out var completionPortThreadsAfterSecondCall);

        Assert.That(workerThreadsAfterSecondCall, Is.EqualTo(workerThreads));
        Assert.That(completionPortThreadsAfterSecondCall, Is.EqualTo(completionPortThreads));
    }
}
