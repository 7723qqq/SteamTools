using DnsClient;
using DnsClient.Protocol;
using System.Runtime.ExceptionServices;
using static BD.WTTS.Services.IDnsAnalysisService;

// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services.Implementation;

sealed class DnsAnalysisServiceImpl
{
    readonly LookupClient lookupClient = new(new LookupClientOptions
    {
        Retries = 0,
        Timeout = TimeSpan.FromSeconds(2),
        UseCache = false,
    });

    // DnsClient caches by question, so sharing a client would let a fallback answer mask the preferred server.
    readonly ConcurrentDictionary<IPAddress, LookupClient> serverClients = new();

    LookupClient GetClient(IPAddress[]? servers)
        => servers is { Length: 1 }
            ? serverClients.GetOrAdd(servers[0], static server => new LookupClient(new LookupClientOptions(server)
            {
                Retries = 0,
                Timeout = TimeSpan.FromSeconds(2),
            }))
            : lookupClient;

    public void ClearDnsCache() => serverClients.Clear();

    public async Task<int> AnalysisHostnameTimeAsync(string url, IPAddress[]? dnsServers = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url))
            return 0;
        var question = new DnsQuestion(url, QueryType.A);
        var client = GetClient(dnsServers);
        var result = dnsServers is { Length: > 1 }
            ? await client.QueryAsync(question, new DnsQueryAndServerOptions(dnsServers) { UseCache = false }, cancellationToken)
            : await client.QueryAsync(question, cancellationToken);
        return result.HasError ? 0 : result.Answers.ARecords().FirstOrDefault()?.InitialTimeToLive ?? 0;
    }

    public IEnumerable<ARecord>? GetPingHostNameData(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            var client = lookupClient;
            var result = client.Query(url, QueryType.A);
            if (result.Answers.Count > 0)
            {
                return result.Answers.ARecords();
            }
        }
        return null;
    }

    public async IAsyncEnumerable<IPAddress> AnalysisDomainIpAsync(string hostNameOrAddress, IPAddress[]? dnsServers, bool isIPv6, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(hostNameOrAddress))
            yield break;
        if (dnsServers == null)
        {
#if NET6_0_OR_GREATER
            var items = await Dns.GetHostAddressesAsync(hostNameOrAddress, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
#else
            var items = await Dns.GetHostAddressesAsync(hostNameOrAddress);
#endif
            foreach (var item in items)
                yield return item;
            yield break;
        }

        var options = dnsServers.Length > 1 ? new DnsQueryAndServerOptions(dnsServers) { UseCache = false } : null;
        var client = GetClient(dnsServers);

        // AAAA 与 A 并行查询，避免两次往返串行累加；输出顺序仍保持 AAAA 优先
        var ipv6Query = isIPv6 ? QuerySafeAsync(client, options, hostNameOrAddress, QueryType.AAAA, cancellationToken) : null;
        var ipv4Query = QuerySafeAsync(client, options, hostNameOrAddress, QueryType.A, cancellationToken);

        var hasIpv6 = false;
        if (ipv6Query != null)
        {
            var (ipv6, ipv6Error) = await ipv6Query;
            ThrowIfCanceled(ipv6Error, cancellationToken);
            if (ipv6 != null && !ipv6.HasError)
                foreach (var item in ipv6.Answers.AaaaRecords())
                {
                    hasIpv6 = true;
                    yield return item.Address;
                }
        }

        var (ipv4, ipv4Error) = await ipv4Query;
        ThrowIfCanceled(ipv4Error, cancellationToken);
        if (ipv4 != null && !ipv4.HasError)
            foreach (var item in ipv4.Answers.ARecords())
                yield return item.Address;
        else if (ipv4Error != null && !hasIpv6)
            ExceptionDispatchInfo.Throw(ipv4Error);
    }

    /// <summary>
    /// 查询失败时把异常作为结果返回，避免并发任务未被观察
    /// </summary>
    static async Task<(IDnsQueryResponse? Response, Exception? Error)> QuerySafeAsync(
        LookupClient client,
        DnsQueryAndServerOptions? options,
        string hostNameOrAddress,
        QueryType queryType,
        CancellationToken cancellationToken)
    {
        try
        {
            var question = new DnsQuestion(hostNameOrAddress, queryType);
            var response = options == null
                ? await client.QueryAsync(question, cancellationToken).ConfigureAwait(false)
                : await client.QueryAsync(question, options, cancellationToken).ConfigureAwait(false);
            return (response, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    static void ThrowIfCanceled(Exception? error, CancellationToken cancellationToken)
    {
        if (error is OperationCanceledException && cancellationToken.IsCancellationRequested)
            ExceptionDispatchInfo.Throw(error);
    }

    public async Task<string?> GetHostByIPAddressAsync(IPAddress ip)
    {
        //var hostName = Dns.GetHostEntry(IPAddress2.Parse(ip)).HostName;
        var client = lookupClient;
        var result = await client.QueryReverseAsync(ip);
        var hostName = result.Answers.PtrRecords().FirstOrDefault()?.PtrDomainName.Value;
        return hostName;
    }

    public async Task<bool> GetIsIpv6SupportAsync()
    {
        try
        {
            var options = new LookupClientOptions
            {
                Retries = 1,
                Timeout = TimeSpan.FromSeconds(1),
                UseCache = false,
            };
            var client = new LookupClient(options);
            var response = await client.QueryServerAsync(
                new[] {
                    IPAddress.Parse(PrimaryDNS_IPV6_Ali),
                },
                IPV6_TESTDOMAIN,
                QueryType.AAAA,
                QueryClass.IN);
            if (response.Answers.AaaaRecords().Any(s =>
                s.Address.Equals(IPAddress.Parse(IPV6_TESTDOMAIN_SUCCESS))))
            {
                return true;
            }
        }
        catch
        {
        }
        return false;
    }
}