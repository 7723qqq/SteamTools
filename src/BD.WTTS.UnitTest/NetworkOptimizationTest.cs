using BD.WTTS.Services.Implementation;

namespace BD.WTTS.UnitTest;

public sealed class NetworkOptimizationTest
{
    [Test]
    public void ScriptResponseBufferStreamsAfterLimit()
    {
        using var destination = new MemoryStream();
        using var stream = new ScriptResponseBufferStream(destination, 4);
        var payload = new byte[] { 1, 2, 3, 4, 5, 6 };

        stream.Write(payload);

        Assert.Multiple(() =>
        {
            Assert.That(stream.Passthrough, Is.True);
            Assert.That(stream.Buffer.Length, Is.Zero);
            Assert.That(destination.ToArray(), Is.EqualTo(payload));
        });
    }
}
