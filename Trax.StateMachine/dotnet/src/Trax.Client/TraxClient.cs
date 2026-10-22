using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>How a <see cref="TraxClient"/> reads answers and follows streams.</summary>
public sealed record TraxClientOptions
{
    /// <summary>The options variables are written and data read with; <see cref="TraxJson.Options"/> when null.</summary>
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
    /// The stream is over and is not subscribed again: the host completed it, refused it (<see cref="Errors"/>), or
    /// refused the connection (<see cref="Failure"/>).
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
    public TraxClient(ITraxTransport transport, TraxClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _json = options?.Json ?? TraxJson.Options;
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
    /// and the stream's interruptions, resumptions and end. A frame with errors ends the stream.
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
            : JsonSerializer.SerializeToNode(variables, _json) as JsonObject
                ?? throw new ArgumentException(
                    "The variables must serialize to a JSON object.",
                    nameof(variables)
                );

    private TraxAnswer<TResult> Read<TResult>(TraxAnswer answer)
    {
        if (answer.Failure is not null)
            return new TraxAnswer<TResult> { Failure = answer.Failure };
        try
        {
            return new TraxAnswer<TResult>
            {
                Data = answer.Data is null ? default : answer.Data.Deserialize<TResult>(_json),
                Errors = answer.Errors,
            };
        }
        catch (JsonException ex)
        {
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
                onEvent(new TraxStreamEvent<TResult>.Resumed());
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
            lock (_gate)
                if (!_wanted || _ended)
                    return;
            onEvent(new TraxStreamEvent<TResult>.Next(answer.Data!));
        }

        public void Error(Exception error)
        {
            switch (error)
            {
                case SubscriptionErrorException refused:
                    End(TraxGraphQLError.ListOf(refused.Errors), null);
                    return;
                case SocketClosedException { Refused: true } closed:
                    End([], new TraxTransportFailure(closed.Message));
                    return;
                case ObjectDisposedException or InvalidOperationException:
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
            onEvent(new TraxStreamEvent<TResult>.Ended(errors, failure));
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
                onEvent(new TraxStreamEvent<TResult>.Interrupted(reason));
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
