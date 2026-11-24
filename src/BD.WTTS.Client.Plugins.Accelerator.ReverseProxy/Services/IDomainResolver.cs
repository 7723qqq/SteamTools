// https://github.com/dotnetcore/FastGithub/blob/2.1.4/FastGithub.DomainResolve/IDomainResolver.cs

// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services;

/// <summary>
/// 域名解析器
/// </summary>
public interface IDomainResolver
{
    /// <summary>
    /// 解析域名
    /// </summary>
    /// <param name="endPoint"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    IAsyncEnumerable<IPAddress> ResolveAsync(DnsEndPoint endPoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查 IPV6 网络支持
    /// </summary>
    /// <param name="cancellationToken"></param>
    void CheckIpv6SupportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录一次对目标域名的连接结果，用于后续按实测耗时排序候选地址
    /// </summary>
    /// <param name="host">目标域名</param>
    /// <param name="address">实际连接的地址</param>
    /// <param name="elapsed">连接耗时，<see langword="null"/> 表示连接失败</param>
    void RecordConnectResult(string host, IPAddress address, TimeSpan? elapsed);

    /// <summary>
    /// 连接失败后失效指定域名的 DNS 与健康缓存
    /// </summary>
    void InvalidateDns(string host);

    /// <summary>
    /// 对所有节点进行测速
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task TestSpeedAsync(CancellationToken cancellationToken = default);
}