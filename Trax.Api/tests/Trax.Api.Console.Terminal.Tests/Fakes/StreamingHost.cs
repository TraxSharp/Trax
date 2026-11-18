using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Connection;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>
/// A host whose streams the test drives: each subscription is held with its sink, so the test can send it a frame,
/// an error or the end. Queries go to <paramref name="inner"/>.
/// </summary>
internal sealed class StreamingHost(ITraxTransport inner) : ITraxTransport
{
    private readonly List<(TraxRequest Request, ISubscriptionSink Sink)> _streams = [];

    public Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    ) => inner.ExecuteAsync(request, cancellationToken);

    private int _subscribed;

    /// <summary>Whether a stream was followed again after it failed, which the client does after an interruption.</summary>
    public bool Interrupted => Volatile.Read(ref _subscribed) > 1;

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink)
    {
        Interlocked.Increment(ref _subscribed);
        lock (_streams)
            _streams.Add((request, sink));
        sink.Started();
        return new Handle(() =>
        {
            lock (_streams)
                _streams.RemoveAll(s => s.Sink == sink);
        });
    }

    /// <summary>The sink of the stream of <paramref name="operation"/> followed now, once it is.</summary>
    public async Task<ISubscriptionSink> StreamAsync(string operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        for (; ; )
        {
            lock (_streams)
                if (
                    _streams
                        .FirstOrDefault(s =>
                            string.Equals(
                                s.Request.Document.Name,
                                operation,
                                StringComparison.Ordinal
                            )
                        )
                        .Sink is
                    { } sink
                )
                    return sink;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Nothing follows {operation}.");
            // allowed-delay: the polling interval of a wait with a deadline.
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class Handle(Action end) : IDisposable
    {
        public void Dispose() => end();
    }
}
