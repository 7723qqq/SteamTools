using System.Reflection;

namespace BD.WTTS.UnitTest;

[NonParallelizable]
public sealed class StartupWatchTraceTest
{
    StringWriter output = null!;
    TextWriter originalOutput = null!;

    [SetUp]
    public void SetUp()
    {
        ResetWatchTrace();
        output = new StringWriter();
        originalOutput = Console.Out;
        Console.SetOut(output);
    }

    [TearDown]
    public void TearDown()
    {
        ResetWatchTrace();
        Console.SetOut(originalOutput);
        output.Dispose();
    }

    [Test]
    public void EarlyTraceIsBufferedUntilDiagnosticsAreConfigured()
    {
        Startup.WatchTrace.Start();
        Startup.WatchTrace.Record("EarlyStage");

        Assert.DoesNotThrow(() => Startup.WatchTrace.StopWriteTotal());
        Assert.That(output.ToString(), Is.Empty);

        Startup.WatchTrace.Configure(true);

        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Contain("EarlyStage"));
            Assert.That(output.ToString(), Does.Contain("Total"));
        });
    }

    [Test]
    public void DisabledDiagnosticsDiscardBufferedTrace()
    {
        Startup.WatchTrace.Start();
        Startup.WatchTrace.Record("EarlyStage");
        Startup.WatchTrace.StopWriteTotal();

        Startup.WatchTrace.Configure(false);

        Assert.That(output.ToString(), Is.Empty);
    }

    static void ResetWatchTrace()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var type = typeof(Startup.WatchTrace);
        type.GetField("sw", flags)!.SetValue(null, null);
        type.GetField("elapsedMilliseconds", flags)!.SetValue(null, 0L);
        type.GetField("configured", flags)!.SetValue(null, false);
        type.GetField("enabled", flags)!.SetValue(null, false);
        ((List<string>)type.GetField("lines", flags)!.GetValue(null)!).Clear();
    }
}
