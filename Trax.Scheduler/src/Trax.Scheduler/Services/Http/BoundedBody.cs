using System.Buffers;

namespace Trax.Scheduler.Services.Http;

/// <summary>
/// Reads a response body up to a limit, for a response requested with
/// <see cref="HttpCompletionOption.ResponseHeadersRead"/>, so an answer larger than the caller
/// accepts is never held in memory whole.
/// </summary>
internal static class BoundedBody
{
    private const int ChunkSize = 81920;

    /// <summary>
    /// Reads at most <paramref name="limit"/> bytes of <paramref name="content"/>.
    /// <c>Exceeded</c> is true when the body is longer, by its declared length or by what arrived;
    /// reading stops there, at the limit.
    /// </summary>
    /// <param name="content">The response content.</param>
    /// <param name="limit">The most bytes to read.</param>
    /// <param name="timeout">How long the body may take, measured from now;
    /// <see cref="Timeout.InfiniteTimeSpan"/> for no limit. Overrunning it throws a
    /// <see cref="TaskCanceledException"/> carrying a <see cref="TimeoutException"/>, as
    /// <see cref="HttpClient.Timeout"/> does for the request.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="prefix">Read the first <paramref name="limit"/> bytes of a longer body, rather
    /// than returning nothing for a body declared longer than the limit.</param>
    public static async Task<(byte[] Bytes, bool Exceeded)> ReadAsync(
        HttpContent content,
        long limit,
        TimeSpan timeout,
        CancellationToken ct,
        bool prefix = false
    )
    {
        if (!prefix && content.Headers.ContentLength is { } declared && declared > limit)
            return ([], true);

        // With ResponseHeadersRead the client's timeout stops at the headers; this one runs on
        // through the body.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout != Timeout.InfiniteTimeSpan)
            deadline.CancelAfter(timeout);

        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            await using var stream = await content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            int read;
            while (
                (read = await stream.ReadAsync(chunk.AsMemory(0, ChunkSize), deadline.Token)) > 0
            )
            {
                var room = limit - buffer.Length;
                if (read > room)
                {
                    buffer.Write(chunk, 0, (int)room);
                    return (buffer.ToArray(), true);
                }
                buffer.Write(chunk, 0, read);
            }
            return (buffer.ToArray(), false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TaskCanceledException(
                $"The response body did not arrive within {timeout.TotalSeconds:0.###} seconds.",
                new TimeoutException(ex.Message, ex)
            );
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }
}
