// ReSharper disable once CheckNamespace
namespace BD.WTTS;

/// <summary>
/// 线程池预热
/// </summary>
static class ThreadPoolHelper
{
    const int MinWorkerThreads = 8;
    const int MinCompletionPortThreads = 8;
    const int MaxWarmThreads = 64;

    /// <summary>
    /// 将线程池最小线程数提升至处理器数量的两倍。
    /// <para>
    /// 运行时默认的最小线程数为处理器数量，之后按每 500 毫秒一个的速率注入新线程；
    /// 本应用存在大量异步回调与后台阻塞任务，突发并发时会因注入速率不足而排队等待，
    /// 因此提前把最小线程数抬到可用值。只提高不降低，已通过运行时配置或
    /// <see cref="ThreadPool.SetMinThreads(int, int)"/> 指定更大值时保持原值。
    /// </para>
    /// </summary>
    internal static void ConfigureMinThreads()
    {
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

        var warmWorkerThreads = Math.Clamp(Environment.ProcessorCount * 2, MinWorkerThreads, MaxWarmThreads);
        var warmCompletionPortThreads = Math.Clamp(Environment.ProcessorCount * 2, MinCompletionPortThreads, MaxWarmThreads);

        if (warmWorkerThreads <= workerThreads && warmCompletionPortThreads <= completionPortThreads)
            return;

        ThreadPool.SetMinThreads(
            Math.Max(workerThreads, warmWorkerThreads),
            Math.Max(completionPortThreads, warmCompletionPortThreads));
    }
}
