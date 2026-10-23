using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

/// <summary>How a <see cref="TraxClient"/> reads answers and follows streams.</summary>
public sealed record TraxClientOptions
{
    /// <summary>
    /// The options variables are written and data read with; <see cref="TraxJson.Options"/> when null. The client
    /// reads them with a copy that leaves every unset <see cref="Optional{T}"/> out of the JSON, whatever their
    /// resolver: a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> included, which
    /// is what a trimmed or native AOT app passes here.
    /// </summary>
    public JsonSerializerOptions? Json { get; init; }

    /// <summary>
    /// Delays before subscribing again after the socket dropped a stream. The last repeats for as long as the stream
    /// is followed. <see cref="TraxClient.DefaultResubscribeDelays"/> when null or empty.
    /// </summary>
    public IReadOnlyList<TimeSpan>? ResubscribeDelays { get; init; }

    /// <summary>The clock the resubscribe delays run on; the system's when null.</summary>
    public TimeProvider? Time { get; init; }
}

/// <summary>What a followed stream delivers.</summary>
public abstract record TraxStreamEvent<T>
{
    private TraxStreamEvent() { }

    /// <summary>One frame's data.</summary>
    public sealed record Next(T Data) : TraxStreamEvent<T>;

    /// <summary>
    /// The socket dropped the stream; it is subscribed again after the resubscribe delays. Frames sent meanwhile are
    /// lost.
    /// </summary>
    public sealed record Interrupted(string Reason) : TraxStreamEvent<T>;

    /// <summary>The stream is subscribed again after an interruption: read again what it may have missed.</summary>
    public sealed record Resumed : TraxStreamEvent<T>;

    /// <summary>
    /// The stream is over and is not subscribed again. The host completed it (neither is set), refused it or answered
    /// a frame with errors (<see cref="Errors"/>); or <see cref="Failure"/> says why: the host refused the connection,
    /// there is no socket to follow it over, a frame did not fit the document's result, or the handler threw.
    /// </summary>
    public sealed record Ended(
        IReadOnlyList<TraxGraphQLError> Errors,
        TraxTransportFailure? Failure
    ) : TraxStreamEvent<T>;
}

/// <summary>
/// Sends documents through an <see cref="ITraxTransport"/>, writing their variables and reading their data with
/// <see cref="TraxJson.Options"/>. Every answer is a value: data, the host's errors, or a transport failure.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/client-api/dotnet-client">SDK reference</seealso>
public sealed class TraxClient
{
    /// <summary>The default delays before subscribing again after the socket dropped a stream.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultResubscribeDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    ];

    private readonly ITraxTransport _transport;
    private readonly JsonSerializerOptions _json;
    private readonly IReadOnlyList<TimeSpan> _delays;
    private readonly TimeProvider _time;

    /// <summary>Builds the client over <paramref name="transport"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The app turned reflection-based serialization off, and <see cref="TraxClientOptions.Json"/> names no resolver.
    /// </exception>
    public TraxClient(ITraxTransport transport, TraxClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _json = TraxJson.LeavingOutUnset(options?.Json);
        _delays = options?.ResubscribeDelays is { Count: > 0 } delays
            ? delays
            : DefaultResubscribeDelays;
        _time = options?.Time ?? TimeProvider.System;
    }

    /// <summary>The transport the client sends through.</summary>
    public ITraxTransport Transport => _transport;

    /// <summary>
    /// Sends a query or mutation. Throws only when <paramref name="cancellationToken"/> is cancelled, or for a
    /// subscription document, which <see cref="Subscribe{TVariables, TResult}"/> follows.
    /// </summary>
    public async Task<TraxAnswer<TResult>> SendAsync<TVariables, TResult>(
        TraxDocument<TVariables, TResult> document,
        TVariables variables,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Kind == TraxOperationKind.Subscription)
            throw new ArgumentException(
                $"{document.Name} is a subscription: follow it with Subscribe.",
                nameof(document)
            );
        var answer = await _transport
            .ExecuteAsync(new TraxRequest(document, VariablesOf(variables)), cancellationToken)
            .ConfigureAwait(false);
        return Read<TResult>(answer);
    }

    /// <summary>
    /// Follows a subscription until the returned handle is disposed. <paramref name="onEvent"/> gets each frame's data,
    /// and the stream's interruptions, resumptions and end, one call at a time. A frame with errors ends the stream. So
    /// does an <paramref name="onEvent"/> that throws: the stream is closed, and its last call is the
    /// <see cref="TraxStreamEvent{T}.Ended"/> whose <see cref="TraxStreamEvent{T}.Ended.Failure"/> names the exception.
    /// Throws an <see cref="ArgumentException"/> for a document that is not a subscription.
    /// </summary>
    public IDisposable Subscribe<TVariables, TResult>(
        TraxDocument<TVariables, TResult> document,
        TVariables variables,
        Action<TraxStreamEvent<TResult>> onEvent
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(onEvent);
        if (document.Kind != TraxOperationKind.Subscription)
            throw new ArgumentException(
                $"{document.Name} is not a subscription: send it with SendAsync.",
                nameof(document)
            );
        var stream = new Stream<TResult>(
            this,
            new TraxRequest(document, VariablesOf(variables)),
            onEvent
        );
        stream.Open();
        return stream;
    }

    private JsonObject VariablesOf<TVariables>(TVariables variables) =>
        variables is null or TraxNoVariables
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(variables, TypeInfo<TVariables>()) as JsonObject
                ?? throw new ArgumentException(
                    "The variables must serialize to a JSON object.",
                    nameof(variables)
                );

    // The contract for T from the client's options: no reflection of its own, so a source-generated context is enough.
    private JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)_json.GetTypeInfo(typeof(T));

    private TraxAnswer<TResult> Read<TResult>(TraxAnswer answer)
    {
        if (answer.Failure is not null)
            return new TraxAnswer<TResult> { Failure = answer.Failure };
        try
        {
            return new TraxAnswer<TResult>
            {
                Data = answer.Data is null ? default : answer.Data.Deserialize(TypeInfo<TResult>()),
                Errors = answer.Errors,
            };
        }
        catch (Exception ex)
        {
            // JsonException for data of another shape; NotSupportedException or InvalidOperationException for a
            // result the options cannot read at all. Either way the answer did not fit, and only this one fails.
            return new TraxAnswer<TResult>
            {
                Errors = answer.Errors,
                Failure = new TraxTransportFailure(
                    $"The host's answer does not fit the document's result: {ex.Message}"
                ),
            };
        }
    }

    // One followed stream: subscribes again after an interruption, until it ends or is disposed.
    private sealed class Stream<TResult>(
        TraxClient client,
        TraxRequest request,
        Action<TraxStreamEvent<TResult>> onEvent
    ) : ISubscriptionSink, IDisposable
    {
        private readonly object _gate = new();

        // Held while onEvent runs, so it is never called from two threads at once.
        private readonly object _deliver = new();
        private bool _wanted = true;
        private bool _ended;
        private bool _interrupted;
        private int _attempt;
        private IDisposable? _current;
        private CancellationTokenSource? _timer;

        public void Open()
        {
            lock (_gate)
            {
                _timer = null;
                if (!_wanted || _ended)
                    return;
            }
            var current = client._transport.Subscribe(request, this);
            bool close;
            lock (_gate)
            {
                close = !_wanted || _ended;
                if (!close)
                    _current = current;
            }
            if (close)
                current.Dispose();
        }

        public void Started()
        {
            bool resumed;
            lock (_gate)
            {
                if (!_wanted || _ended)
                    return;
                resumed = _interrupted;
                _interrupted = false;
                _attempt = 0;
            }
            if (resumed)
                Deliver(new TraxStreamEvent<TResult>.Resumed());
        }

        public void Next(JsonObject value)
        {
            var answer = client.Read<TResult>(
                TraxAnswer.Of(value)
                    ?? TraxAnswer.Failed("The host sent a frame that is not a GraphQL result.")
            );
            if (answer.Failure is not null || answer.Errors.Count > 0)
            {
                End(answer.Errors, answer.Failure);
                return;
            }
            Deliver(new TraxStreamEvent<TResult>.Next(answer.Data!));
        }

        public void Error(Exception error)
        {
            switch (error)
            {
                case SubscriptionErrorException refused:
                    var errors = TraxGraphQLError.ListOf(refused.Errors);
                    End(
                        errors,
                        errors.Count == 0 ? new TraxTransportFailure(refused.Message) : null
                    );
                    return;
                case SocketClosedException { Refused: true } closed:
                    End([], new TraxTransportFailure(closed.Message, closed.HttpStatus));
                    return;
                case SocketUnavailableException:
                    End([], new TraxTransportFailure(error.Message));
                    return;
                default:
                    Again(error.Message);
                    return;
            }
        }

        public void Complete() => End([], null);

        private void End(IReadOnlyList<TraxGraphQLError> errors, TraxTransportFailure? failure)
        {
            IDisposable? close;
            lock (_gate)
            {
                if (!_wanted || _ended)
                    return;
                _ended = true;
                close = _current;
                _current = null;
            }
            close?.Dispose();
            Deliver(new TraxStreamEvent<TResult>.Ended(errors, failure), last: true);
        }

        // Hands one event to onEvent, unless the stream is over. A handler that throws ends the stream, so it is told
        // rather than losing every event after; a throw from the last event has nowhere left to go.
        private void Deliver(TraxStreamEvent<TResult> streamEvent, bool last = false)
        {
            Exception? thrown = null;
            lock (_deliver)
            {
                lock (_gate)
                    if (!_wanted || (_ended && !last))
                        return;
                try
                {
                    onEvent(streamEvent);
                }
                catch (Exception ex) when (!last)
                {
                    thrown = ex;
                }
                catch (Exception)
                {
                    // The stream is over and its handler was told; there is nobody left to tell.
                }
            }
            if (thrown is not null)
                End([], new TraxTransportFailure($"The stream's handler threw: {thrown.Message}"));
        }

        // The socket dropped the stream: say so once, and subscribe again after the next delay.
        private void Again(string reason)
        {
            IDisposable? close;
            TimeSpan delay;
            CancellationTokenSource timer;
            bool first;
            lock (_gate)
            {
                if (!_wanted || _ended)
                    return;
                close = _current;
                _current = null;
                first = !_interrupted;
                _interrupted = true;
                delay = client._delays[Math.Min(_attempt, client._delays.Count - 1)];
                _attempt++;
                timer = _timer = new CancellationTokenSource();
            }
            close?.Dispose();
            if (first)
                Deliver(new TraxStreamEvent<TResult>.Interrupted(reason));
            _ = Task.Delay(delay, client._time, timer.Token)
                .ContinueWith(
                    t =>
                    {
                        if (!t.IsCanceled)
                            Open();
                    },
                    TaskScheduler.Default
                );
        }

        public void Dispose()
        {
            IDisposable? close;
            CancellationTokenSource? timer;
            lock (_gate)
            {
                _wanted = false;
                close = _current;
                _current = null;
                timer = _timer;
                _timer = null;
            }
            timer?.Cancel();
            close?.Dispose();
        }
    }
}
