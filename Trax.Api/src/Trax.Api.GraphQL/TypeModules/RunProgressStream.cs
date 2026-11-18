using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.Types;
using Trax.Core.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// One runProgress stream: the run's changes of state and its steps, read from the two lifecycle topics, kept to the
/// run, passed through its visibility and numbered as one sequence that skips a number after any loss on either topic.
/// It ends after the run ends, and releases both topics and its place once.
/// </summary>
internal sealed class RunProgressStream : ISourceStream<RunProgressEvent>
{
    private readonly RunProgressRun? _run;
    private readonly ISourceStream<TrainLifecycleEvent>? _states;
    private readonly ISourceStream<JunctionEvent>? _steps;
    private readonly long _statesBaseline;
    private readonly long _stepsBaseline;
    private readonly IDisposable? _lease;
    private readonly RunProgressEvent? _only;
    private readonly Func<CancellationToken, ValueTask<long>>? _stepsLastPublished;

    /// <summary>Where a failed run's retry is looked up, after the run ends.</summary>
    public Func<
        RunProgressRun,
        CancellationToken,
        Task<(string? RetriedAs, bool NoRetry)>
    >? NextTry { get; init; }

    /// <summary>What became of a run first found queued, read while it waits.</summary>
    public Func<RunProgressRun, CancellationToken, Task<QueuedFate>>? ReadQueued { get; init; }

    /// <summary>Where a failed read of a queued run is logged.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>The clock the stream waits on and stamps a run's missing end with.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>How the stream waits between two reads; replaced in tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>How long a failed run of a manifest waits for its retry before the stream ends without one.</summary>
    internal static TimeSpan RetryWait { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long the stream waits before it first looks for the retry again. It doubles up to <see cref="RetryPollMax"/>.</summary>
    internal static TimeSpan RetryPoll { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between two looks for the retry.</summary>
    internal static TimeSpan RetryPollMax { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often a run first found queued is read while it waits, so one cancelled before dispatch, which publishes no
    /// lifecycle event, still ends its stream.
    /// </summary>
    internal static TimeSpan QueuedPoll { get; set; } = TimeSpan.FromSeconds(5);

    // Cancelled when the stream is disposed, so a read the subscription never cancels (a retry wait, a queued poll)
    // does not outlive the stream's place.
    private readonly CancellationTokenSource _disposing = new();
    private int _disposed;

    public RunProgressStream(RunProgressEvent only) => _only = only;

    public RunProgressStream(
        RunProgressRun run,
        ISourceStream<TrainLifecycleEvent> states,
        long statesBaseline,
        ISourceStream<JunctionEvent> steps,
        long stepsBaseline,
        Func<CancellationToken, ValueTask<long>> stepsLastPublished,
        IDisposable lease
    )
    {
        _stepsLastPublished = stepsLastPublished;
        _run = run;
        _states = states;
        _statesBaseline = statesBaseline;
        _steps = steps;
        _stepsBaseline = stepsBaseline;
        _lease = lease;
    }

    /// <summary>
    /// The authorization the stream was admitted on, asked again while it runs; null for a stream
    /// that answers once.
    /// </summary>
    public SubscriptionRecheck? Recheck { get; init; }

    public IAsyncEnumerable<RunProgressEvent> ReadEventsAsync() =>
        Read<RunProgressEvent>(CancellationToken.None);

    IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() =>
        Read<object?>(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await _disposing.CancelAsync().ConfigureAwait(false);
        if (_states is not null)
            await _states.DisposeAsync().ConfigureAwait(false);
        if (_steps is not null)
            await _steps.DisposeAsync().ConfigureAwait(false);
        _lease?.Dispose();
    }

    private async IAsyncEnumerable<TOut> Read<TOut>(
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        if (_only is not null)
        {
            yield return (TOut)(object)_only;
            yield break;
        }

        var events = Events(ct);
        if (Recheck is { } recheck)
        {
            var sequence = 0L;
            await foreach (
                var next in recheck
                    .Until(
                        events,
                        () =>
                            new RunProgressEvent(
                                _run.NotNull().RunId,
                                sequence + 1,
                                State: null,
                                Step: null,
                                Failure: null,
                                RunProgressTypeModule.NotFound()
                            ),
                        ct
                    )
                    .WithCancellation(ct)
                    .ConfigureAwait(false)
            )
            {
                sequence = next.Sequence;
                yield return (TOut)(object)next;
            }
            yield break;
        }

        await foreach (var next in events.WithCancellation(ct).ConfigureAwait(false))
            yield return (TOut)(object)next;
    }

    private async IAsyncEnumerable<RunProgressEvent> Events(
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var run = _run.NotNull();
        var merged = Channel.CreateUnbounded<(
            TrainLifecycleEvent? State,
            JunctionEvent? Step,
            bool Lost
        )>();
        var stepsSeen = new StrongBox<long>(_stepsBaseline);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposing.Token);
        var pumps = Task.WhenAll(
            Pump(
                _states.NotNull(),
                new StrongBox<long>(_statesBaseline),
                e => e.PublishSequence,
                e => (e, null),
                merged.Writer,
                stop.Token
            ),
            Pump(
                _steps.NotNull(),
                stepsSeen,
                e => e.PublishSequence,
                e => (null, e),
                merged.Writer,
                stop.Token
            )
        );
        _ = pumps.ContinueWith(_ => merged.Writer.TryComplete(), TaskScheduler.Default);
        // A queued run cancelled before dispatch publishes nothing, so its fate is read while it waits.
        var closed =
            run.State == RunProgressState.Queued
                ? WatchQueued(run, merged.Writer, stop.Token)
                : null;

        var sequence = 0L;
        try
        {
            // The state the run was in when it was found comes first, so a stream opened after the run ended still
            // says how it ended, and then the stream ends.
            sequence++;
            yield return new RunProgressEvent(
                run.RunId,
                sequence,
                run.State,
                Step: null,
                run.Failure,
                Problem: null
            );
            if (IsEnd(run.State))
            {
                if (run.RetriedAs is { } known)
                    yield return new RunProgressEvent(
                        run.RunId,
                        ++sequence,
                        State: null,
                        Step: null,
                        Failure: null,
                        Problem: null,
                        known
                    );
                else
                    await foreach (var retry in RetryOf(run, stop.Token).ConfigureAwait(false))
                        yield return new RunProgressEvent(
                            run.RunId,
                            ++sequence,
                            State: null,
                            Step: null,
                            Failure: null,
                            Problem: null,
                            retry
                        );
                yield break;
            }

            RunProgressEvent? end = null;
            await foreach (var item in merged.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                if (closed is not null && item is { State: null, Step: null, Lost: false })
                {
                    if (await closed.ConfigureAwait(false) is { } last)
                        yield return (last with { Sequence = ++sequence });
                    yield break;
                }
                if (Next(run, item, ref sequence, out end) is { } next)
                    yield return next;
                if (end is not null)
                    break;
            }
            if (end is null)
                yield break;

            // The run published its steps before its end, but on another topic, which may be read later. The steps
            // published until the end was read are waited for, briefly: a step dropped on the way never arrives, and
            // counts as a loss instead.
            var target = await _stepsLastPublished.NotNull()(stop.Token).ConfigureAwait(false);
            using (var settle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                settle.CancelAfter(SettleBound);
                while (Volatile.Read(ref stepsSeen.Value) < target)
                {
                    try
                    {
                        await Task.Delay(SettlePoll, Clock, settle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                    {
                        sequence++;
                        break;
                    }
                }
            }
            while (merged.Reader.TryRead(out var item))
                if (item.Step is not null && Next(run, item, ref sequence, out _) is { } step)
                    yield return step;

            yield return (end with { Sequence = ++sequence });

            await foreach (
                var retry in RetryOf(run with { State = end.State.NotNull().Value }, stop.Token)
                    .ConfigureAwait(false)
            )
                yield return new RunProgressEvent(
                    run.RunId,
                    ++sequence,
                    State: null,
                    Step: null,
                    Failure: null,
                    Problem: null,
                    retry
                );
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            if (closed is not null)
                try
                {
                    await closed.ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException or ChannelClosedException)
                {
                    // The watch only ends this way once the stream is stopping.
                }
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    // A failed run of a manifest is retried by the scheduler after its delay: the id of the retry once it is queued,
    // and nothing when the manifest is dead-lettered, when RetryWait passes, or when the run did not fail.
    private async IAsyncEnumerable<string> RetryOf(
        RunProgressRun run,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        if (run.State != RunProgressState.Failed || NextTry is null)
            yield break;
        var failed = run with
        {
            EndedAt = run.EndedAt ?? Clock.GetUtcNow().UtcDateTime.AddSeconds(-5),
        };
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waiting.CancelAfter(RetryWait);
        // Backed off, so many streams waiting on retries that are minutes away do not poll the database every second.
        var poll = RetryPoll;
        while (true)
        {
            (string? RetriedAs, bool NoRetry) next;
            try
            {
                next = await NextTry(failed, waiting.Token).ConfigureAwait(false);
                if (next.RetriedAs is null && !next.NoRetry)
                {
                    await Delay(poll, waiting.Token).ConfigureAwait(false);
                    poll = poll * 2 < RetryPollMax ? poll * 2 : RetryPollMax;
                }
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            if (next.RetriedAs is { } retry)
            {
                yield return retry;
                yield break;
            }
            if (next.NoRetry)
                yield break;
        }
    }

    // Reads a run first found queued every QueuedPoll until it is dispatched, whose lifecycle events then say the rest,
    // or until it is cancelled or gone, which publish none. Then it writes an empty item to wake the reader and answers
    // the event to end on. A read that fails is tried again after a wait that doubles up to RetryPollMax, and after
    // QueuedFailureBound failures in a row the stream ends with a problem, so the client is told and subscribes again.
    private async Task<RunProgressEvent?> WatchQueued(
        RunProgressRun run,
        ChannelWriter<(TrainLifecycleEvent?, JunctionEvent?, bool)> writer,
        CancellationToken ct
    )
    {
        if (ReadQueued is null)
            return null;
        try
        {
            var wait = QueuedPoll;
            var failures = 0;
            while (true)
            {
                await Delay(wait, ct).ConfigureAwait(false);
                QueuedFate fate;
                try
                {
                    fate = await ReadQueued(run, ct).ConfigureAwait(false);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    // A dropped connection, a read that timed out and the like: the run is read again later, so a
                    // cancel after it still ends the stream and releases its place.
                    if (++failures >= QueuedFailureBound)
                    {
                        Logger?.LogWarning(
                            e,
                            "Reading queued run {RunId} failed {Failures} times in a row; ending its stream.",
                            run.RunId,
                            failures
                        );
                        await writer.WriteAsync((null, null, false), ct).ConfigureAwait(false);
                        return new RunProgressEvent(
                            run.RunId,
                            0,
                            State: null,
                            Step: null,
                            Failure: null,
                            Unreadable()
                        );
                    }
                    Logger?.LogWarning(
                        e,
                        "Reading queued run {RunId} failed; reading it again.",
                        run.RunId
                    );
                    wait = wait * 2 < RetryPollMax ? wait * 2 : RetryPollMax;
                    continue;
                }
                failures = 0;
                wait = QueuedPoll;
                if (fate == QueuedFate.Dispatched)
                    return null;
                if (fate == QueuedFate.Waiting)
                    continue;
                var last =
                    fate == QueuedFate.Cancelled
                        ? new RunProgressEvent(
                            run.RunId,
                            0,
                            RunProgressState.Cancelled,
                            Step: null,
                            Failure: null,
                            Problem: null
                        )
                        : new RunProgressEvent(
                            run.RunId,
                            0,
                            State: null,
                            Step: null,
                            Failure: null,
                            RunProgressTypeModule.NotFound()
                        );
                await writer.WriteAsync((null, null, false), ct).ConfigureAwait(false);
                return last;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>How many reads of a queued run may fail in a row before its stream ends with a problem.</summary>
    internal const int QueuedFailureBound = 10;

    internal static SnapshotProblem Unreadable() =>
        new()
        {
            Code = RefusalCode.InternalError,
            Message = "The run could not be read. Subscribe again.",
        };

    /// <summary>How long the end of a run waits for its last steps before reporting them lost.</summary>
    internal static readonly TimeSpan SettleBound = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(5);

    private static bool IsEnd(RunProgressState? state) =>
        state
            is RunProgressState.Completed
                or RunProgressState.Failed
                or RunProgressState.Cancelled;

    // One item of the merged topics as this run's starter sees it: a step to send, nothing, or the run's end, which is
    // held in end (unnumbered) rather than sent, so its last steps can go first.
    private static RunProgressEvent? Next(
        RunProgressRun run,
        (TrainLifecycleEvent? State, JunctionEvent? Step, bool Lost) item,
        ref long sequence,
        out RunProgressEvent? end
    )
    {
        end = null;
        if (item.Lost)
        {
            sequence++;
            return null;
        }
        if (item.Step is { } step)
        {
            if (
                !string.Equals(step.ExternalId, run.RunId, StringComparison.Ordinal)
                || run.Visibility.Present(step) is not { } shownStep
            )
                return null;
            return new RunProgressEvent(
                run.RunId,
                ++sequence,
                State: null,
                shownStep.Junction,
                Failure: null,
                Problem: null
            );
        }
        if (
            item.State is not { } state
            || !string.Equals(state.ExternalId, run.RunId, StringComparison.Ordinal)
            || run.Visibility.Present(state) is not { } shown
        )
            return null;
        var now = RunProgressAccess.StateOf(shown.TrainState);
        var failure = string.Equals(
            shown.FailureReason,
            LifecycleSubscriptionAccess.MaskedFailureReason,
            StringComparison.Ordinal
        )
            ? null
            : shown.FailureReason;
        var answer = new RunProgressEvent(run.RunId, 0, now, Step: null, failure, Problem: null);
        if (IsEnd(now))
        {
            end = answer;
            return null;
        }
        return answer with { Sequence = ++sequence };
    }

    // Reads one topic into the merged channel, marking a jump in its publish numbers as a loss. The highest number
    // read is recorded only after its event is in the channel, so a reader that sees it may read the event.
    private static async Task Pump<T>(
        ISourceStream<T> stream,
        StrongBox<long> seen,
        Func<T, long> publishSequence,
        Func<T, (TrainLifecycleEvent?, JunctionEvent?)> wrap,
        ChannelWriter<(TrainLifecycleEvent?, JunctionEvent?, bool)> writer,
        CancellationToken ct
    )
    {
        var last = seen.Value;
        try
        {
            await foreach (
                var e in stream.ReadEventsAsync().WithCancellation(ct).ConfigureAwait(false)
            )
            {
                var published = publishSequence(e);
                if (published > last + 1)
                    await writer.WriteAsync((null, null, true), ct).ConfigureAwait(false);
                var (state, step) = wrap(e);
                await writer.WriteAsync((state, step, false), ct).ConfigureAwait(false);
                if (published > last)
                {
                    last = published;
                    Volatile.Write(ref seen.Value, last);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
