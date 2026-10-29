using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>The codes a client reports of its own, beside the server's refusal codes.</summary>
public static class ClientProblemCodes
{
    /// <summary>The instance was deleted, or the server holds none the caller may read.</summary>
    public const string Gone = "GONE";

    /// <summary>The request never reached the server, or its answer never came back (offline, a 5xx, an auth error).</summary>
    public const string TransportError = "TRANSPORT_ERROR";

    /// <summary>A retry was asked for, but nothing has failed since the last one was retried.</summary>
    public const string NothingToRetry = "NOTHING_TO_RETRY";

    /// <summary>The controller was disposed before the server answered.</summary>
    public const string Disposed = "DISPOSED";
}

/// <summary>Whether a problem is the server's refusal or a request that did not get through.</summary>
public enum MachineProblemKind
{
    /// <summary>The server, or the controller on its behalf, refused.</summary>
    Rejected,

    /// <summary>The request did not reach the server, or its answer did not come back.</summary>
    Transport,
}

/// <summary>The last refusal or transport failure, shown until the next event the server confirms.</summary>
/// <param name="Kind">Whether it was refused or never got through.</param>
/// <param name="Code">The contract: a refusal code (<c>GUARD_FAILED</c>) or one of <see cref="ClientProblemCodes"/>.</param>
/// <param name="Message">What to tell a person. It is not part of the contract.</param>
public sealed record MachineProblem(MachineProblemKind Kind, string Code, string Message);

/// <summary>
/// Why a controller stopped for good: the instance is gone, this client is out of date and must reload, or the host
/// refused the socket.
/// </summary>
/// <param name="Code">
/// <see cref="ClientProblemCodes.Gone"/>, <c>CLIENT_OUTDATED</c>, or the code of a stream that ended for good
/// (<see cref="StreamMessage.Refused"/>): <c>UNAUTHENTICATED</c> or <c>FORBIDDEN</c> for a socket the host refused,
/// <see cref="ClientProblemCodes.TransportError"/> for a push the client cannot read.
/// </param>
/// <param name="Message">What to tell a person.</param>
/// <param name="Deleted">
/// With <see cref="ClientProblemCodes.Gone"/>: true when the instance was deleted or expired, false when the server
/// answered <c>NOT_FOUND</c>, which does not tell a missing instance from another caller's. Null otherwise.
/// </param>
public sealed record MachineError(string Code, string Message, bool? Deleted = null);

/// <summary>Where a controller is.</summary>
public enum MachineStatus
{
    /// <summary>No snapshot from the server yet.</summary>
    Loading,

    /// <summary>Showing what the server confirmed, and any prediction on top of it.</summary>
    Ready,

    /// <summary>Stopped for good; see <see cref="MachineView.Error"/>.</summary>
    Stopped,
}

/// <summary>What a controller holds, as one immutable value: a new one on every change.</summary>
public sealed record MachineView
{
    /// <summary>Loading until the first snapshot from the server, stopped once gone or outdated.</summary>
    public MachineStatus Status { get; init; }

    /// <summary>The instance's id, once started or given.</summary>
    public string? Id { get; init; }

    /// <summary>What to show: the newest prediction while an event is pending, else what the server confirmed.</summary>
    public Snapshot? Snapshot { get; init; }

    /// <summary>The last snapshot the server confirmed.</summary>
    public Snapshot? Confirmed { get; init; }

    /// <summary>The incarnation of the instance the view holds.</summary>
    public string? RowId { get; init; }

    /// <summary>The revision of <see cref="Confirmed"/>.</summary>
    public long? Revision { get; init; }

    /// <summary>When the state's timer is due, as the server says; null when it has none. The client never fires it.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>Whether an event is in flight or queued.</summary>
    public bool Pending { get; init; }

    /// <summary>The last refusal or transport failure, until the next event the server confirms.</summary>
    public MachineProblem? Problem { get; init; }

    /// <summary>Why the controller stopped, once it has.</summary>
    public MachineError? Error { get; init; }
}

/// <summary>What one send came to. Never an exception.</summary>
public abstract record SendOutcome
{
    private SendOutcome() { }

    /// <summary>The server applied it and stored <see cref="Snapshot"/>.</summary>
    public sealed record Confirmed(ServerSnapshot Snapshot) : SendOutcome;

    /// <summary>The server refused it.</summary>
    public sealed record Refused(MachineProblem Problem) : SendOutcome;

    /// <summary>It did not get through, or the server could not take it yet; <see cref="ServerMachineController.RetryAsync"/> resends it.</summary>
    public sealed record Failed(MachineProblem Problem) : SendOutcome;

    /// <summary>The controller stopped for good before or while it was sent.</summary>
    public sealed record Stopped(MachineError Error) : SendOutcome;
}

/// <summary>How a <see cref="ServerMachineController"/> is built.</summary>
public sealed record ServerMachineOptions
{
    /// <summary>The machine as the host's client manifest lists it: its id, IR and IR hash.</summary>
    public required ClientManifestMachine Machine { get; init; }

    /// <summary>The server's operations and change stream for that machine.</summary>
    public required IMachineServer Server { get; init; }

    /// <summary>
    /// The engine to predict with (<see cref="MachinePredictor.For{TState,TTrigger}"/>). Without one, an event is sent
    /// with no prediction and the view shows only what the server confirmed. Its IR hash must be the manifest's.
    /// </summary>
    public IMachinePredictor? Predictor { get; init; }

    /// <summary>An existing instance to open. Without it, <see cref="ServerMachineController.StartAsync"/> creates one.</summary>
    public string? Id { get; init; }

    /// <summary>The start's input, when the machine's start takes one.</summary>
    public JsonNode? StartInput { get; init; }

    /// <summary>
    /// The request id of the start, minted when null. A controller that replaces a disposed one passes the same id, so
    /// the server answers the retried start with the instance it already created.
    /// </summary>
    public string? StartRequestId { get; init; }

    /// <summary>
    /// Mints a request id; a new GUID by default. A custom one must mint 1 to 200 characters, not blank, with no control
    /// character: the server answers any other <c>INVALID_REQUEST_ID</c>.
    /// </summary>
    public Func<string>? NewRequestId { get; init; }

    /// <summary>
    /// Delays between retries of a <c>SERVER_OUTDATED</c> answer (another node of a rolling deploy answered) and of a read
    /// the transport failed. After the last, the event fails and <see cref="ServerMachineController.RetryAsync"/> resends it.
    /// </summary>
    public IReadOnlyList<TimeSpan>? RetryDelays { get; init; }
}

/// <summary>
/// Drives one instance of a machine a server owns. The server applies every transition; the client only predicts.
/// </summary>
/// <remarks>
/// <para><b>Events</b> are sent one at a time, each under its own request id, in the order they were sent. While one is
/// pending the view shows the prediction, which is sent beside it; the server's answer replaces it, and a refusal or a
/// transport failure goes back to what the server confirmed. An event the predictor refuses is still sent, without a
/// prediction. An event bound to the machine's effect is sent with no prediction, and nothing after it is predicted
/// until it answers; so is an event with an edge that rests on a custom rule the predictor holds no handler for (a
/// predictor built from the IR holds none).</para>
/// <para><b>Ordering.</b> Every snapshot, pushed or answered, carries its revision, and one at or below the revision held
/// is dropped. An answer without one (a store that records none) is taken and cannot be ordered: the next snapshot, with a
/// revision or without, is taken after it. The instance is read again only after a reload push, a new incarnation or a
/// <c>CLIENT_DIVERGENCE</c>.</para>
/// <para><b>Retries.</b> <c>SERVER_OUTDATED</c> is retried under the same request id after each of the retry delays. A
/// transport failure ends the event as failed; <see cref="RetryAsync"/> resends it under the same request id, so the
/// server replays it rather than applying it twice.</para>
/// <para><b>Stopping.</b> A gone instance and <c>CLIENT_OUTDATED</c> stop the controller for good.</para>
/// <para><b>Threads.</b> The controller does its work on one exclusive scheduler, so it handles one thing at a time
/// whichever thread calls it. <see cref="Changed"/> is raised there: a handler reads <see cref="View"/> and must not
/// block on the controller. A UI app marshals to its own thread from the handler. A handler that throws is reported to
/// <see cref="Trace"/> and does not stop the controller or the handlers after it.</para>
/// <para>Every operation is total: none throws for a refusal or a failed request.</para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/dotnet-client">SDK reference</seealso>
public sealed class ServerMachineController : IDisposable
{
    /// <summary>The default delays between retries of a <c>SERVER_OUTDATED</c> answer or a failed read.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    private static readonly string ClientOutdated = RefusalCode.ClientOutdated.ToWire();
    private static readonly string ServerOutdated = RefusalCode.ServerOutdated.ToWire();
    private static readonly string ClientDivergence = RefusalCode.ClientDivergence.ToWire();
    private static readonly string NotFound = RefusalCode.NotFound.ToWire();

    private readonly TaskScheduler _loop =
        new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
    private readonly MachineShape _shape;
    private readonly IMachineServer _server;
    private readonly IMachinePredictor? _predictor;
    private readonly JsonNode? _startInput;
    private readonly Func<string> _newRequestId;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly object _startGate = new();

    private string? _id;
    private string? _startRequestId;
    private MachineStatus _status = MachineStatus.Loading;
    private Snapshot? _confirmed;
    private string? _rowId;
    private long? _revision;
    private DateTimeOffset? _dueAt;
    private MachineProblem? _problem;
    private MachineError? _error;

    private readonly List<QueuedEvent> _queue = [];
    private bool _sending;
    private EventRequest? _lastFailed;
    private bool _startFailed;
    private readonly List<TaskCompletionSource<SendOutcome>> _startWaiters = [];

    private bool _disposed;
    private Task? _started;
    private IDisposable? _stream;
    private CancellationTokenSource _timers = new();

    private bool _reloading;
    private string? _reloadRowId;

    // Set by a CLIENT_DIVERGENCE: nothing more is sent, predicted from the snapshot that diverged, until the instance
    // has been read again.
    private bool _resyncing;
    private bool _reloadAgain;
    private ServerSnapshot? _buffered;

    private MachineView _view;

    /// <summary>Builds a controller. It sends nothing until <see cref="StartAsync"/>.</summary>
    /// <exception cref="ArgumentException">The predictor is of another version of the machine than the manifest.</exception>
    public ServerMachineController(ServerMachineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _shape = MachineShape.Of(options.Machine);
        _server = options.Server;
        _predictor = options.Predictor;
        if (_predictor is not null && _predictor.IrHash != options.Machine.IrHash)
            throw new ArgumentException(
                $"The predictor's machine '{_shape.Id}' (IR {_predictor.IrHash}) is another version than the manifest's "
                    + $"(IR {options.Machine.IrHash}). Export the manifest again from a host that runs this version.",
                nameof(options)
            );
        _id = options.Id;
        _startInput = options.StartInput;
        _startRequestId = options.StartRequestId;
        _newRequestId = options.NewRequestId ?? (() => Guid.NewGuid().ToString());
        _retryDelays = options.RetryDelays ?? DefaultRetryDelays;
        _view = BuildView();
    }

    /// <summary>
    /// Raised after every change of <see cref="View"/>, on the controller's scheduler, once the sends the change settled
    /// are settled. A handler that throws is reported to <see cref="Trace"/>; the other handlers still run.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The current view. The same object until something changes.</summary>
    public MachineView View => Volatile.Read(ref _view);

    /// <summary>
    /// Opens the instance: subscribes first, then reads it, so no write falls between the two. Without an id it starts
    /// one under the start request id and subscribes to the id the server minted. Calling it again returns the same
    /// task; a start that failed is resent by <see cref="RetryAsync"/>.
    /// </summary>
    public Task StartAsync()
    {
        lock (_startGate)
            return _started ??= Post(OpenAsync);
    }

    /// <summary>Sends one event under a fresh request id, after every earlier event of this instance has settled.</summary>
    /// <param name="trigger">The trigger's name, as the machine declares it (<c>Coin</c>).</param>
    /// <param name="input">The trigger's input, or null when it takes none.</param>
    public Task<SendOutcome> SendAsync(string trigger, JsonNode? input = null)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return Post(() =>
        {
            if (_error is not null)
                return Task.FromResult<SendOutcome>(new SendOutcome.Stopped(_error));
            if (_disposed)
                return Task.FromResult(DisposedOutcome());
            return Enqueue(new QueuedEvent(trigger, input?.DeepClone()));
        });
    }

    /// <summary>
    /// Resends what last failed under the same request id, so the server replays its recorded result if the first
    /// attempt did arrive: the start while there is no instance, else the last event that failed, unless a later event
    /// has been confirmed since.
    /// </summary>
    public Task<SendOutcome> RetryAsync() =>
        Post(() =>
        {
            if (_error is not null)
                return Task.FromResult<SendOutcome>(new SendOutcome.Stopped(_error));
            if (_disposed)
                return Task.FromResult(DisposedOutcome());
            if (_startFailed && _id is null && _startRequestId is not null)
            {
                _startFailed = false;
                var waiter = NewOutcome();
                _startWaiters.Add(waiter);
                _ = CreateAsync(_startRequestId, 0);
                return waiter.Task;
            }
            var request = _lastFailed;
            if (request is null)
                return Task.FromResult<SendOutcome>(
                    new SendOutcome.Failed(
                        Refusal(
                            ClientProblemCodes.NothingToRetry,
                            "No event has failed since the last one was retried."
                        )
                    )
                );
            _lastFailed = null;
            return Enqueue(new QueuedEvent(request.Trigger, request.Input) { Request = request });
        });

    /// <summary>Whether the predictor allows <paramref name="trigger"/> from what is shown. False before the first snapshot and once stopped.</summary>
    /// <remarks>
    /// Without a predictor, true for any trigger with an edge out of the shown state: the server decides. An event bound to
    /// the effect is judged by its edge alone, since its guard reads an input only the server's effect has, and so is one
    /// whose edge rests on a custom rule the predictor holds no handler for.
    /// </remarks>
    public bool CanSend(string trigger, JsonNode? input = null)
    {
        var view = View;
        if (view.Snapshot is not { } shown || view.Status == MachineStatus.Stopped)
            return false;
        if (!Predicts(shown.State, trigger))
            return Available(shown).Contains(trigger);
        return _predictor.CanFire(shown, trigger, input);
    }

    /// <summary>The triggers with an edge out of what is shown; none before the first snapshot or once stopped.</summary>
    public IReadOnlyList<string> AvailableTriggers()
    {
        var view = View;
        if (view.Snapshot is not { } shown || view.Status == MachineStatus.Stopped)
            return [];
        return Available(shown);
    }

    private IReadOnlyList<string> Available(Snapshot shown) =>
        _predictor?.AvailableTriggers(shown)
        ?? _shape.TriggersFrom.GetValueOrDefault(shown.State)
        ?? [];

    /// <summary>Closes the stream and drops every late answer. Pending sends settle as failed with <see cref="ClientProblemCodes.Disposed"/>.</summary>
    public void Dispose() => _ = Post(DisposeCore);

    // ── The scheduler ──────────────────────────────────────────────────────────────────────────

    // Runs work on the exclusive scheduler. An await inside it resumes there too, so the controller's state is only ever
    // touched by one piece of work at a time, as on a JavaScript event loop.
    private Task Post(Func<Task> work) =>
        Task
            .Factory.StartNew(
                work,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                _loop
            )
            .Unwrap();

    private Task<T> Post<T>(Func<Task<T>> work) =>
        Task
            .Factory.StartNew(
                work,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                _loop
            )
            .Unwrap();

    private Task Post(Action work) =>
        Task.Factory.StartNew(
            work,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            _loop
        );

    private static TaskCompletionSource<SendOutcome> NewOutcome() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void DisposeCore()
    {
        if (_disposed)
            return;
        _disposed = true;
        Close();
        _timers.Cancel();
        var queued = _queue.ToList();
        _queue.Clear();
        foreach (var entry in queued)
            entry.Settle.TrySetResult(DisposedOutcome());
        SettleStart(DisposedOutcome());
        Changed = null;
    }

    // ── Opening ────────────────────────────────────────────────────────────────────────────────

    private async Task OpenAsync()
    {
        if (_disposed || _error is not null)
            return;
        if (_id is not null)
        {
            Listen(_id);
            await ReadAsync(null);
            return;
        }
        _startRequestId ??= _newRequestId();
        await CreateAsync(_startRequestId, 0);
    }

    private async Task CreateAsync(string requestId, int attempt)
    {
        if (_disposed || _error is not null)
            return;
        ServerAnswer answer;
        try
        {
            answer = Answered(await _server.StartAsync(_startInput?.DeepClone(), requestId));
        }
        catch (Exception ex)
        {
            if (_disposed)
                return;
            _problem = TransportFailure(ex);
            _startFailed = true;
            Emit();
            SettleStart(new SendOutcome.Failed(_problem));
            return;
        }
        if (_disposed)
            return;
        if (answer.Problem is { } problem)
        {
            if (problem.Code == ServerOutdated && attempt < _retryDelays.Count)
            {
                // Another node of a rolling deploy answered: another may run this version.
                Later(attempt, () => CreateAsync(requestId, attempt + 1), () => Task.CompletedTask);
                return;
            }
            if (problem.Code == ClientOutdated)
            {
                Stop(new MachineError(ClientOutdated, problem.Message));
                SettleStart(new SendOutcome.Stopped(_error!));
                return;
            }
            _problem = Refusal(problem.Code, problem.Message);
            var retryable = problem.Code == ServerOutdated;
            _startFailed = retryable;
            Emit();
            SettleStart(
                retryable ? new SendOutcome.Failed(_problem) : new SendOutcome.Refused(_problem)
            );
            return;
        }
        if (answer.Id is not { } id)
        {
            _problem = TransportFailure(
                new InvalidOperationException(
                    "The server started an instance without answering its id."
                )
            );
            _startFailed = true;
            Emit();
            SettleStart(new SendOutcome.Failed(_problem));
            return;
        }
        _id = id;
        _problem = null;
        Listen(id);
        // The stream's first event is the instance as it is then, so a write between the answer and the subscription
        // reaches it there; the answer is the snapshot to show until then.
        Accept(answer.Snapshot!, Source.Answer);
        Emit();
        SettleStart(new SendOutcome.Confirmed(answer.Snapshot!));
        Pump();
    }

    private void SettleStart(SendOutcome outcome)
    {
        var waiters = _startWaiters.ToList();
        _startWaiters.Clear();
        foreach (var waiter in waiters)
            waiter.TrySetResult(outcome);
    }

    private void Listen(string id)
    {
        var open = true;
        var stream = _server.Subscribe(
            id,
            message =>
                Post(() =>
                {
                    if (open && !_disposed && _error is null)
                        OnStream(message);
                })
        );
        _stream = new Closer(() =>
        {
            if (!open)
                return;
            open = false;
            stream.Dispose();
        });
    }

    private void Close()
    {
        var stream = _stream;
        _stream = null;
        stream?.Dispose();
    }

    private void OnStream(StreamMessage message)
    {
        switch (message)
        {
            case StreamMessage.Changed changed:
                if (Accept(changed.Snapshot, Source.Push))
                    Emit();
                Pump();
                return;
            case StreamMessage.Gone gone:
                Stop(
                    gone.NotFound
                        ? new MachineError(
                            ClientProblemCodes.Gone,
                            "The server holds no such instance for this caller.",
                            Deleted: false
                        )
                        : new MachineError(
                            ClientProblemCodes.Gone,
                            "The instance was deleted or expired.",
                            Deleted: true
                        )
                );
                return;
            case StreamMessage.ClientOutdated:
                Stop(
                    new MachineError(
                        ClientOutdated,
                        "The server no longer serves this version of the machine. Reload."
                    )
                );
                return;
            case StreamMessage.Refused refused:
                Stop(new MachineError(refused.Code, refused.Message));
                return;
            default:
                // A reload, or a message this version does not know: read the instance again.
                Reload(null);
                return;
        }
    }

    // ── Reading ────────────────────────────────────────────────────────────────────────────────

    private void Reload(string? rowId)
    {
        if (_reloading)
        {
            _reloadAgain = true;
            return;
        }
        _reloadRowId = rowId;
        _ = ReadAsync(rowId);
    }

    private async Task ReadAsync(string? rowId, int attempt = 0)
    {
        if (_disposed || _error is not null || _id is null)
            return;
        _reloading = true;
        _reloadRowId = rowId;
        var id = _id;
        ServerAnswer answer;
        try
        {
            answer = Answered(await _server.GetAsync(id));
        }
        catch (Exception ex)
        {
            if (_disposed || _error is not null)
                return;
            _problem = TransportFailure(ex);
            Emit();
            Later(
                attempt,
                () => ReadAsync(rowId, attempt + 1),
                () =>
                {
                    EndRead();
                    Pump();
                    return Task.CompletedTask;
                }
            );
            return;
        }
        if (_disposed || _error is not null)
            return;

        if (answer.Problem is { } problem)
        {
            if (problem.Code == ServerOutdated && attempt < _retryDelays.Count)
            {
                Later(
                    attempt,
                    () => ReadAsync(rowId, attempt + 1),
                    () =>
                    {
                        EndRead();
                        Pump();
                        return Task.CompletedTask;
                    }
                );
                return;
            }
            EndRead();
            if (problem.Code == ClientOutdated)
                Stop(new MachineError(ClientOutdated, problem.Message));
            else if (problem.Code == NotFound)
                Stop(new MachineError(ClientProblemCodes.Gone, problem.Message, Deleted: false));
            else
            {
                _problem = Refusal(problem.Code, problem.Message);
                Emit();
                Pump();
            }
            return;
        }
        var snapshot = answer.Snapshot! with { RowId = answer.Snapshot.RowId ?? rowId ?? _rowId };
        var changed = Accept(snapshot, Source.Load);
        var buffered = _buffered;
        _buffered = null;
        if (buffered is not null && _error is null && buffered.RowId == _rowId)
            changed = Accept(buffered, Source.Push) || changed;
        EndRead();
        if (changed && _error is null)
            Emit();
        Pump();
    }

    private void EndRead()
    {
        _reloading = false;
        _reloadRowId = null;
        if (_reloadAgain && !_disposed && _error is null)
        {
            _reloadAgain = false;
            Reload(null);
            return;
        }
        _resyncing = false;
    }

    // ── Ordering ───────────────────────────────────────────────────────────────────────────────

    private enum Source
    {
        Push,
        Answer,
        Load,
    }

    // Whether a snapshot at `incoming` is at or below one at `held`. A missing revision on either side cannot be ordered,
    // so it is never older.
    private static bool OlderOrSame(long? incoming, long? held) =>
        incoming is { } i && held is { } h && i <= h;

    // Takes a server snapshot if it is newer than the one held; returns whether the view changed. A push of another
    // incarnation than the one held starts one read instead, and is kept until that read answers.
    private bool Accept(ServerSnapshot incoming, Source source)
    {
        var rowId = incoming.RowId;
        var otherIncarnation = rowId is not null && _rowId is not null && rowId != _rowId;

        if (otherIncarnation && source != Source.Load)
        {
            // The row was deleted and created again: its revisions restart, so they say nothing against the ones held.
            if (!_reloading)
                Reload(rowId);
            else if (_reloadRowId is null)
                _reloadAgain = true;
            if (
                _buffered is null
                || _buffered.RowId != rowId
                || !OlderOrSame(incoming.Revision, _buffered.Revision)
            )
                _buffered = incoming;
            return false;
        }
        if (!otherIncarnation && OlderOrSame(incoming.Revision, _revision))
        {
            // A read of the revision held, asked for because an answer did not know the due time of its timed state,
            // brings the due time and nothing else.
            if (
                source == Source.Load
                && incoming.Revision == _revision
                && _dueAt is null
                && incoming.DueAtKnown
                && incoming.DueAt is not null
                && _confirmed?.State == incoming.State
            )
            {
                _dueAt = incoming.DueAt;
                return true;
            }
            return false;
        }

        var snapshot = new Snapshot
        {
            Machine = _shape.Id,
            Version = _shape.Version,
            State = incoming.State,
            Context = (JsonObject)incoming.Context.DeepClone(),
        };
        if (_predictor is not null)
        {
            var parsed = _predictor.Rehydrate(_predictor.Serialize(snapshot));
            if (parsed is not RehydrationResult.Ok ok)
            {
                // The server serves a snapshot this client's machine cannot read: it was built for another version.
                var error = (RehydrationResult.Error)parsed;
                Stop(
                    new MachineError(
                        ClientOutdated,
                        $"The server's snapshot is not one this client's machine can read ({error.Code.ToWire()}): {error.Message}"
                    )
                );
                return false;
            }
            snapshot = ok.Snapshot;
        }

        // The due time is the server's: a known one replaces the one held, null included. An answer that does not know
        // it keeps the one held only while the instance stays in the state it was given for; in any other state a timed
        // state is read once for it.
        var sameState =
            !otherIncarnation && _confirmed is not null && _confirmed.State == snapshot.State;
        var timed = _shape.TimedStates.Contains(snapshot.State);
        if (incoming.DueAtKnown)
            _dueAt = incoming.DueAt;
        else if (sameState && _dueAt is not null)
        {
            // Kept: the same state, and the answer does not know better.
        }
        else
        {
            _dueAt = null;
            if (timed && source != Source.Load)
                Reload(null);
        }
        _confirmed = snapshot;
        _rowId = rowId ?? _rowId;
        _revision = incoming.Revision;
        if (_status == MachineStatus.Loading)
            _status = MachineStatus.Ready;
        return true;
    }

    // ── Sending ────────────────────────────────────────────────────────────────────────────────

    private sealed record EventRequest(
        string Id,
        string Trigger,
        JsonNode? Input,
        string RequestId,
        string? ClientResult
    );

    private sealed class QueuedEvent(string trigger, JsonNode? input)
    {
        public string Trigger { get; } = trigger;
        public JsonNode? Input { get; } = input;

        // Fixed once the event is first sent, so a retry carries exactly the same request.
        public EventRequest? Request { get; set; }
        public TaskCompletionSource<SendOutcome> Settle { get; } = NewOutcome();
    }

    private Task<SendOutcome> Enqueue(QueuedEvent entry)
    {
        _queue.Add(entry);
        Pump();
        Emit();
        return entry.Settle.Task;
    }

    private void Pump()
    {
        if (
            _sending
            || _resyncing
            || _disposed
            || _error is not null
            || _id is null
            || _confirmed is null
        )
            return;
        if (_queue.Count == 0)
            return;
        var next = _queue[0];
        _sending = true;

        if (next.Request is null)
        {
            // An event bound to the effect is sent without a prediction: what it leads to depends on the effect's result. So
            // is one that rests on a custom rule the predictor cannot evaluate, rather than a guess the server would refuse.
            var predicted = !Predicts(_confirmed.State, next.Trigger)
                ? null
                : _predictor.Advance(_confirmed, next.Trigger, next.Input?.DeepClone());
            next.Request = new EventRequest(
                _id,
                next.Trigger,
                next.Input,
                _newRequestId(),
                predicted is AdvanceResult.Transitioned t ? _predictor!.Serialize(t.Snapshot) : null
            );
        }
        _ = DeliverAsync(next, next.Request, 0);
    }

    private async Task DeliverAsync(QueuedEvent entry, EventRequest request, int attempt)
    {
        ServerAnswer answer;
        try
        {
            answer = Answered(
                await _server.EventAsync(
                    request.Id,
                    request.Trigger,
                    request.Input?.DeepClone(),
                    request.RequestId,
                    request.ClientResult
                )
            );
        }
        catch (Exception ex)
        {
            if (_disposed || _error is not null)
                return;
            _lastFailed = request;
            _problem = TransportFailure(ex);
            Finish(entry, new SendOutcome.Failed(_problem));
            return;
        }
        if (_disposed || _error is not null)
            return;

        if (answer.Problem is not { } problem)
        {
            var snapshot = answer.Snapshot!;
            Accept(snapshot, Source.Answer);
            // A confirmed event supersedes one that failed before it: resending that one now would apply it out of order.
            _lastFailed = null;
            _problem = null;
            Finish(entry, new SendOutcome.Confirmed(snapshot));
            return;
        }

        if (problem.Code == ClientOutdated)
        {
            Stop(new MachineError(ClientOutdated, problem.Message));
            return;
        }
        if (problem.Code == ServerOutdated)
        {
            if (attempt < _retryDelays.Count)
            {
                Later(
                    attempt,
                    () => DeliverAsync(entry, request, attempt + 1),
                    () => Task.CompletedTask
                );
                return;
            }
            _lastFailed = request;
            _problem = Refusal(problem.Code, problem.Message);
            Finish(entry, new SendOutcome.Failed(_problem));
            return;
        }
        if (problem.Code == ClientDivergence)
        {
            // The predictor and the server disagree about this step: what the client holds cannot be trusted, and the
            // next event would be predicted from it too, so it waits for the read.
            _resyncing = true;
            Reload(null);
        }
        _problem = Refusal(problem.Code, problem.Message);
        Finish(entry, new SendOutcome.Refused(_problem));
    }

    private void Finish(QueuedEvent entry, SendOutcome outcome)
    {
        _queue.Remove(entry);
        _sending = false;
        // The view the outcome is read against goes out first; the handlers hear of it once the queue has moved on.
        Publish();
        entry.Settle.TrySetResult(outcome);
        Pump();
        Emit();
    }

    // ── Stopping and timing ────────────────────────────────────────────────────────────────────

    private void Stop(MachineError error)
    {
        if (_error is not null || _disposed)
            return;
        _error = error;
        _status = MachineStatus.Stopped;
        Close();
        _timers.Cancel();
        _timers = new CancellationTokenSource();
        _sending = false;
        var stranded = _queue.ToList();
        _queue.Clear();
        Publish();
        foreach (var queued in stranded)
            queued.Settle.TrySetResult(new SendOutcome.Stopped(error));
        Emit();
    }

    // Runs `next` after the attempt's delay, or `exhausted` at once when there is none left.
    private void Later(int attempt, Func<Task> next, Func<Task> exhausted)
    {
        if (attempt >= _retryDelays.Count)
        {
            _ = exhausted();
            return;
        }
        var token = _timers.Token;
        _ = DelayThen(_retryDelays[attempt], token, next);
    }

    private async Task DelayThen(TimeSpan delay, CancellationToken token, Func<Task> next)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!_disposed && _error is null)
            await next();
    }

    // An answer with neither a snapshot nor a problem is not one: it fails as a transport failure would.
    private static ServerAnswer Answered(ServerAnswer answer) =>
        answer.Problem is null && answer.Snapshot is null
            ? throw new InvalidOperationException(
                "The server answered without a snapshot or a problem."
            )
            : answer;

    private static SendOutcome DisposedOutcome() =>
        new SendOutcome.Failed(
            Refusal(
                ClientProblemCodes.Disposed,
                "The controller was disposed before the server answered."
            )
        );

    private static MachineProblem Refusal(string code, string message) =>
        new(MachineProblemKind.Rejected, code, message);

    private static MachineProblem TransportFailure(Exception ex) =>
        new(
            MachineProblemKind.Transport,
            ClientProblemCodes.TransportError,
            string.IsNullOrEmpty(ex.Message) ? "The server could not be reached." : ex.Message
        );

    // Publishes the view and tells every handler. Never throws: a handler's exception is its own, and must not leave a
    // send unsettled or the queue stuck behind it.
    private void Emit()
    {
        if (_disposed)
            return;
        Publish();
        if (Changed is not { } changed)
            return;
        foreach (var handler in changed.GetInvocationList())
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"A ServerMachineController.Changed handler threw: {ex}");
            }
        }
    }

    private void Publish()
    {
        if (!_disposed)
            Volatile.Write(ref _view, BuildView());
    }

    private MachineView BuildView() =>
        new()
        {
            Status = _status,
            Id = _id,
            Snapshot = Predicted(),
            Confirmed = _confirmed,
            RowId = _rowId,
            Revision = _revision,
            DueAt = _dueAt,
            Pending = _queue.Count > 0,
            Problem = _problem,
            Error = _error,
        };

    // Whether the predictor can tell what `trigger` leads to from `state`: there is one, the trigger is not bound to the
    // effect, and no edge it could take rests on a custom rule the predictor holds no handler for.
    [MemberNotNullWhen(true, nameof(_predictor))]
    private bool Predicts(string state, string trigger) =>
        _predictor is not null
        && !_shape.EffectTriggers.Contains(trigger)
        && (_predictor is not IPartialPredictor partial || partial.Predicts(state, trigger));

    // The confirmed snapshot with every pending event the predictor allows applied in order, up to the first one bound to
    // the effect or resting on a custom rule it cannot evaluate: what the page will show if the server agrees.
    private Snapshot? Predicted()
    {
        if (_confirmed is null || _status == MachineStatus.Stopped || _predictor is null)
            return _confirmed;
        var shown = _confirmed;
        foreach (var queued in _queue)
        {
            if (!Predicts(shown.State, queued.Trigger))
                break;
            if (
                _predictor.Advance(shown, queued.Trigger, queued.Input?.DeepClone())
                is AdvanceResult.Transitioned next
            )
                shown = next.Snapshot;
        }
        return shown;
    }

    private sealed class Closer(Action close) : IDisposable
    {
        public void Dispose() => close();
    }
}
