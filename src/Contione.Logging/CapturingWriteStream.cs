namespace Contione.Logging;

internal sealed class CapturingWriteStream(Stream inner, int limit) : Stream
{
    private readonly MemoryStream _capture = new();

    public int Count => (int)_capture.Length;
    public bool ExceededLimit => Count > limit;
    public ReadOnlyMemory<byte> Content => _capture.GetBuffer().AsMemory(0, Count);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        Capture(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        Capture(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        Capture(buffer.Span);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await inner.WriteAsync(buffer, offset, count, cancellationToken);
        Capture(buffer.AsSpan(offset, count));
    }

    private void Capture(ReadOnlySpan<byte> buffer)
    {
        var remaining = limit + 1 - Count;
        if (remaining > 0)
            _capture.Write(buffer[..Math.Min(buffer.Length, remaining)]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _capture.Dispose();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _capture.Dispose();
        return ValueTask.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
