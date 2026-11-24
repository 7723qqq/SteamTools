namespace BD.WTTS.Services.Implementation;

/// <summary>
/// 记录每个域名的连接结果，并按实测往返时间对候选地址排序
/// </summary>
/// <remarks>
/// 数据来自真实连接，不额外发起探测连接：成功记录往返时间，失败降权到末尾，
/// 超过有效期后重新视为未知，避免一次偶发失败长期影响排序。
/// </remarks>
sealed class ConnectHealthStore
{
    const long UnknownScore = 1L << 40;
    const long KnownMaxScore = UnknownScore - 1;
    const long FailedScore = 1L << 41;

    /// <summary>
    /// 记录过期时间
    /// </summary>
    readonly TimeSpan entryTtl;
    static readonly long CleanupIntervalTicks = Stopwatch.Frequency * TimeSpan.FromMinutes(1).Seconds;

    readonly ConcurrentDictionary<(string Host, IPAddress Address), Entry> entries = new();
    long lastCleanupTimestamp = -CleanupIntervalTicks;

    public ConnectHealthStore(TimeSpan? entryTtl = null)
        => this.entryTtl = entryTtl ?? TimeSpan.FromMinutes(10);

    /// <summary>
    /// 记录一次连接结果
    /// </summary>
    /// <param name="host">目标域名</param>
    /// <param name="address">实际连接的地址</param>
    /// <param name="elapsed">连接耗时，<see langword="null"/> 表示连接失败</param>
    public void Record(string host, IPAddress address, TimeSpan? elapsed)
    {
        entries[(host, address)] = new Entry(elapsed ?? TimeSpan.Zero, elapsed == null, Stopwatch.GetTimestamp());
    }

    public void Remove(string host)
    {
        foreach (var pair in entries)
        {
            if (string.Equals(pair.Key.Host, host, StringComparison.OrdinalIgnoreCase))
                entries.TryRemove(pair);
        }
    }

    public void Clear() => entries.Clear();

    /// <summary>
    /// 按历史连接结果排序：成功且快的优先，未知次之，失败或过期记录的靠后
    /// </summary>
    public IReadOnlyList<IPAddress> Order(string host, IReadOnlyList<IPAddress> addresses)
    {
        var now = Stopwatch.GetTimestamp();
        var ttl = (long)(entryTtl.TotalSeconds * Stopwatch.Frequency);
        if (now - Interlocked.Read(ref lastCleanupTimestamp) >= CleanupIntervalTicks)
        {
            Interlocked.Exchange(ref lastCleanupTimestamp, now);
            foreach (var pair in entries)
            {
                if (now - pair.Value.Timestamp > ttl)
                    entries.TryRemove(pair);
            }
        }

        if (addresses.Count < 2)
            return addresses;

        return addresses.OrderBy(x => Score((host, x), entries, now, ttl)).ToArray();
    }

    static long Score((string Host, IPAddress Address) key, ConcurrentDictionary<(string Host, IPAddress Address), Entry> entries, long now, long ttl)
    {
        if (!entries.TryGetValue(key, out var entry) || now - entry.Timestamp > ttl)
            return UnknownScore;
        if (entry.Failed)
            return FailedScore;
        return (long)Math.Min(entry.Rtt.TotalMilliseconds, KnownMaxScore);
    }

    readonly record struct Entry(TimeSpan Rtt, bool Failed, long Timestamp);
}
