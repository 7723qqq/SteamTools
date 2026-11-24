using System.Buffers;

namespace BD.WTTS.Services.Implementation;

internal sealed class ScriptResponseBufferStream : Stream
{
    readonly Stream destination;
    readonly int maxSize;
    byte[]? buffer;
    int length;
    bool passthrough;

    public ScriptResponseBufferStream(Stream destination, int maxSize)
    {
        this.destination = destination;
        this.maxSize = maxSize;
    }

    public MemoryStream Buffer => buffer == null
        ? new MemoryStream()
        : new MemoryStream(buffer, 0, length, writable: false, publiclyVisible: true);
    public bool Passthrough => passthrough;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => length;
    public override long Position
    {
        get => length;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
        => WriteCore(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> source)
        => WriteCore(source);

    void WriteCore(ReadOnlySpan<byte> source)
    {
        if (passthrough)
        {
            destination.Write(source);
            return;
        }
        if (source.Length > maxSize - length)
        {
            passthrough = true;
            destination.Write(buffer.AsSpan(0, length));
            ReturnBuffer();
            destination.Write(source);
            return;
        }
        EnsureCapacity(source.Length);
        source.CopyTo(buffer.AsSpan(length));
        length += source.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        if (passthrough)
        {
            await destination.WriteAsync(source, cancellationToken);
            return;
        }
        if (source.Length > maxSize - length)
        {
            passthrough = true;
            await destination.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
            ReturnBuffer();
            await destination.WriteAsync(source, cancellationToken);
            return;
        }
        EnsureCapacity(source.Length);
        source.CopyTo(buffer.AsMemory(length));
        length += source.Length;
    }

    void EnsureCapacity(int additionalLength)
    {
        var required = length + additionalLength;
        if (buffer != null && required <= buffer.Length)
            return;

        var capacity = Math.Min(maxSize, Math.Max(required, Math.Max(4096, (buffer?.Length ?? 0) * 2)));
        var rented = ArrayPool<byte>.Shared.Rent(capacity);
        if (length > 0)
            buffer.AsSpan(0, length).CopyTo(rented);
        if (buffer != null)
            ArrayPool<byte>.Shared.Return(buffer);
        buffer = rented;
    }

    public async ValueTask CopyToOriginalAsync()
    {
        if (!passthrough)
            await destination.WriteAsync(buffer.AsMemory(0, length));
    }

    void ReturnBuffer()
    {
        if (buffer != null)
            ArrayPool<byte>.Shared.Return(buffer);
        buffer = null;
        length = 0;
    }

    public override void Flush()
    {
        if (passthrough)
            destination.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
        => passthrough ? destination.FlushAsync(cancellationToken) : Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReturnBuffer();
        base.Dispose(disposing);
    }
}
