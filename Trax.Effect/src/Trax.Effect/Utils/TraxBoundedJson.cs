using System.Text;
using System.Text.Json;

namespace Trax.Effect.Utils;

/// <summary>
/// Serializes a train's input or output under a byte ceiling, the way every copy of a parameter
/// that Trax keeps or hands on is bounded.
/// </summary>
public static class TraxBoundedJson
{
    /// <summary>
    /// The JSON written in place of a value whose serialization crossed <paramref name="maxBytes"/>.
    /// </summary>
    public static string TruncatedPlaceholder(int maxBytes) =>
        $$"""{"_truncated": true, "_maxBytes": {{maxBytes}}}""";

    /// <summary>
    /// Serializes <paramref name="value"/> as its runtime type, or returns
    /// <see cref="TruncatedPlaceholder"/> if it would exceed <paramref name="maxBytes"/> UTF-8
    /// bytes. <c>null</c> <paramref name="maxBytes"/> is unbounded.
    /// </summary>
    /// <remarks>
    /// With a ceiling it serializes through the <c>Stream</c> overload, which flushes to the
    /// underlying stream incrementally: the bytes are counted as they are written and serialization
    /// stops the moment the ceiling is crossed, so an oversized payload is never fully
    /// materialized. The runtime type is passed explicitly so a boxed value serializes
    /// polymorphically. A value System.Text.Json cannot represent still throws
    /// <see cref="JsonException"/> or <see cref="NotSupportedException"/>.
    /// </remarks>
    public static string Serialize(object value, JsonSerializerOptions options, int? maxBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        if (maxBytes is not int cap)
            return JsonSerializer.Serialize(value, value.GetType(), options);

        try
        {
            using var buffer = new MemoryStream();
            using (var ceiling = new ByteCeilingStream(buffer, cap))
                JsonSerializer.Serialize(ceiling, value, value.GetType(), options);

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (PayloadTooLargeException)
        {
            return TruncatedPlaceholder(cap);
        }
    }

    private sealed class PayloadTooLargeException(int maxBytes)
        : Exception($"Serialized parameter exceeded {maxBytes} bytes.");

    /// <summary>
    /// A write-only stream that forwards to an inner stream until a byte ceiling is crossed,
    /// then throws <see cref="PayloadTooLargeException"/>.
    /// </summary>
    /// <remarks>
    /// The ceiling behavior is validated behaviorally through <see cref="Serialize"/>. The rest is
    /// <see cref="Stream"/> contract plumbing the serializer never calls on a write-only stream, so
    /// it is excluded from coverage.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private sealed class ByteCeilingStream(Stream inner, int maxBytes) : Stream
    {
        private long _written;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _written += buffer.Length;
            if (_written > maxBytes)
                throw new PayloadTooLargeException(maxBytes);
            inner.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;

        public override void Flush() => inner.Flush();

        public override long Length => _written;
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
