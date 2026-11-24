// ReSharper disable once CheckNamespace
namespace BD.WTTS;

partial class Startup // 启动耗时跟踪
{
#if STARTUP_WATCH_TRACE || DEBUG
    /// <summary>
    /// 启动耗时跟踪
    /// </summary>
    /// <remarks>
    /// 记录先缓存，停止时再按设置决定是否输出到控制台。
    /// 早期阶段在设置加载之前就已记录，直接输出会丢掉最前面的耗时。
    /// </remarks>
    public static class WatchTrace
    {
        static Stopwatch? sw;
        static long elapsedMilliseconds;
        static readonly List<string> lines = new(32);
        static bool configured;
        static bool enabled;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Write(string line)
        {
            Debug.WriteLine(line);
            lines.Add(line);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Flush()
        {
            if (!configured || lines.Count == 0) return;

            if (enabled)
                foreach (var line in lines)
                    DebugConsole.WriteLine(line);

            lines.Clear();
        }

        internal static void Configure(bool enabled)
        {
            WatchTrace.enabled = enabled;
            configured = true;
            Flush();
        }

        /// <summary>
        /// 停止计时器，并按设置输出已记录的耗时
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Stop()
        {
            sw?.Stop();
            Flush();
        }

        /// <summary>
        /// 启动计时器
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Start()
        {
            sw?.Stop();
            sw = Stopwatch.StartNew();
        }

        /// <summary>
        /// 根据标记记录耗时
        /// </summary>
        /// <param name="mark"></param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Record(string mark)
        {
            if (sw == null) return;

            sw.Stop();
            elapsedMilliseconds += sw.ElapsedMilliseconds;
            Write($"{mark} {sw.ElapsedMilliseconds}ms");
            sw.Restart();
        }

        /// <summary>
        /// 停止计时并显示总计耗时
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void StopWriteTotal()
        {
            if (sw == null) return;

            sw.Stop();
            elapsedMilliseconds += sw.ElapsedMilliseconds;
            Write($"Total {elapsedMilliseconds}ms");
            Flush();
        }
    }
#endif
}
