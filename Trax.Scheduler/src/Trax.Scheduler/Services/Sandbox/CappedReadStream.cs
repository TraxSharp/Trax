namespace Trax.Scheduler.Services.Sandbox;

/// <summary>A read-only view of a stream that throws once more than a set number of bytes have been read through it.</summary>
internal sealed class CappedReadStream(Stream inner, long cap) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Count(int read)
    {
        _read += read;
        if (_read > cap)
            throw new ReadCapExceededException();
        return read;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
