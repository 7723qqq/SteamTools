// https://github.com/dotnetcore/FastGithub/blob/2.1.4/FastGithub.FlowAnalyze/FlowAnalyzer.cs

// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services.Implementation;

sealed class FlowAnalyzer : IFlowAnalyzer
{
    const int INTERVAL_SECONDS = 5;
    const int CounterCount = 64;

    readonly FlowQueues readQueues = new(INTERVAL_SECONDS);
    readonly FlowQueues writeQueues = new(INTERVAL_SECONDS);

    /// <summary>
    /// 收到数据
    /// </summary>
    /// <param name="flowType"></param>
    /// <param name="length"></param>
    public void OnFlow(FlowType flowType, int length)
    {
        if (flowType == FlowType.Read)
            readQueues.OnFlow(length);
        else
            writeQueues.OnFlow(length);
    }

    /// <summary>
    /// 获取流量分析
    /// </summary>
    /// <returns></returns>
    public FlowStatistics GetFlowStatistics() => new()
    {
        TotalRead = readQueues.TotalBytes,
        TotalWrite = writeQueues.TotalBytes,
        ReadRate = readQueues.GetRate(),
        WriteRate = writeQueues.GetRate(),
    };

    sealed class FlowQueues
    {
        readonly Counter[] counters = new Counter[CounterCount];
        readonly int intervalSeconds;
        long totalBytes;

        public long TotalBytes => Volatile.Read(ref totalBytes);

        public FlowQueues(int intervalSeconds)
        {
            this.intervalSeconds = intervalSeconds;
        }

        public void OnFlow(int length)
        {
            var ticks = Environment.TickCount64 / 1000;
            ref var counter = ref counters[(uint)Environment.CurrentManagedThreadId % CounterCount];
            if (Volatile.Read(ref counter.Ticks) != ticks)
            {
                Volatile.Write(ref counter.Ticks, ticks);
                Volatile.Write(ref counter.Length, 0);
            }
            Interlocked.Add(ref counter.Length, length);
            Interlocked.Add(ref totalBytes, length);
        }

        public double GetRate()
        {
            var now = Environment.TickCount64 / 1000;
            long length = 0;
            for (var i = 0; i < counters.Length; i++)
            {
                ref var counter = ref counters[i];
                var ticks = Volatile.Read(ref counter.Ticks);
                if (ticks != 0 && now - ticks < intervalSeconds)
                    length += Volatile.Read(ref counter.Length);
            }
            return length / (double)intervalSeconds;
        }

        struct Counter
        {
            public long Ticks;
            public long Length;
        }
    }
}
