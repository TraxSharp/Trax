using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Exceptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Services.Decisions;

namespace Trax.Effect.Data.Decisions;

/// <summary>
/// Writes each decision a run makes to <c>trax.decision</c> as it is made, and serves a requeued
/// run the answers its original gave. Registered by <c>AddDecisionRecording</c> as both the
/// <see cref="IDecisionObserver"/> and the <see cref="IDecisionReplay"/> Trax.Core looks for.
/// </summary>
/// <remarks>
/// <para>A decision is written before the train acts on it, through a data context of its own,
/// so a run that dies mid-way (an OOM kill, a timeout, a deploy) leaves every decision it acted on
/// behind for its requeue to replay, and the write never flushes or inherits what the run's own
/// junctions have tracked. The journal is <see cref="Required"/>: a decision that cannot be written
/// fails its step before any track is taken, classified transient, because a database that is down
/// now may not be on a retry. A value the store refuses is classified permanent.</para>
///
/// <para>The journal itself holds nothing between runs. Each run's row and replayed answers live
/// on the run's own async flow, set by <c>ServiceTrain.Run</c> when it starts and dropped when its
/// junctions finish, so two runs that share an external id each write under their own row and
/// replay their own answers, and nothing is left behind however a run ends. A decision made
/// outside a service train's run, or in a run that was never persisted, is logged only. One the
/// run's own train reports under an external id other than the run's, because the train's
/// <c>ExternalId</c> was changed while it ran, fails its step, classified permanent, rather than
/// going unrecorded.</para>
///
/// <para>Code inside a run can lose the run's async flow, by suppressing
/// <see cref="ExecutionContext"/> flow, say, so a decision, refusal, routing or replay lookup can
/// arrive with no run on its flow. One reported by a train that has begun a recorded run on this
/// host is then looked up by its external id: when exactly one run of that train is in progress
/// under it, it is recorded against, or replayed from, that run. Otherwise it is logged only, as
/// before. A plain train run inside a junction never begins a run, so its decisions are never
/// looked up this way.</para>
/// </remarks>
public sealed class DecisionJournal(
    IDataContextProviderFactory contextFactory,
    ILogger<DecisionJournal>? logger = null
) : IDecisionObserver, IDecisionReplay, IDecisionRunRecorder
{
    // Optional, so a host that registers no logging still records decisions instead of failing
    // every run that makes one.
    private readonly ILogger _logger = logger ?? NullLogger<DecisionJournal>.Instance;

    private static readonly IReadOnlyDictionary<(string, int), RecordedAnswer> NothingToReplay =
        new Dictionary<(string, int), RecordedAnswer>();

    /// <summary>
    /// The row names each train has begun recorded runs under on this host, keyed by the name
    /// Trax.Core reports the train's decisions under, so a decision that arrives without its run's
    /// flow can be looked up by the train's own rows only. It grows with the trains, not the runs.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _rowNames =
        new();

    /// <summary>
    /// True: a requeue replays what was written, so a decision that could not be written must not
    /// be acted on.
    /// </summary>
    public bool Required => true;

    /// <inheritdoc />
    public async Task Decided(DecisionMade decision, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Train {Train} (run {RunId}) decided {Question}: {Answer} by {Decider}{Replayed}{Shadows}{Refused}",
            decision.Train,
            decision.RunId,
            decision.Question.Key,
            Describe(decision.Answer),
            decision.Decider?.Name ?? "replay",
            decision.Replayed ? " (replayed)" : "",
            decision.Shadows.Count == 0
                ? ""
                : $"; shadows agreeing: {decision.Shadows.Count(s => s.Agrees)}/{decision.Shadows.Count}",
            decision.ReplayRefused is null ? "" : $"; not replayed: {decision.ReplayRefused}"
        );

        if (
            await Bound(decision.Train, decision.RunId, replay: false, cancellationToken)
            is not { MetadataId: { } metadataId } run
        )
            return;

        RecordedDecision record;

        try
        {
            record = Record(decision, metadataId);
        }
        catch (NotSupportedException e)
        {
            // An answer that cannot be written would be one a requeue could not replay, and it is
            // the same on every retry.
            throw DecisionRun.Classified(e, decision.Train, decision.RunId, FailureClass.Permanent);
        }

        await Write(
            decision.Train,
            decision.RunId,
            async context =>
            {
                context.RecordedDecisions.Add(record);
                await context.SaveChanges(cancellationToken);
            },
            cancellationToken
        );

        run.Latest[record.QuestionKey] = record.Id;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Written as a row with <see cref="RecordedDecision.Refused"/> set, so the answer the run would
    /// not act on, and why, sit next to the failed step. A refused row is never replayed: the run
    /// that failed on it is a different run from its requeue, which asks the question afresh. An
    /// answer that cannot be written as JSON is recorded as null, with the reason saying so.
    /// </remarks>
    public async Task Refused(DecisionRefused refusal, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Train {Train} (run {RunId}) refused the answer to {Question}: {Answer} by {Decider}, because {Reason}",
            refusal.Train,
            refusal.RunId,
            refusal.Question.Key,
            refusal.Answer is null ? "no answer" : Describe(refusal.Answer),
            refusal.Decider.Name,
            refusal.Reason
        );

        if (
            await Bound(refusal.Train, refusal.RunId, replay: false, cancellationToken)
            is not { MetadataId: { } metadataId }
        )
            return;

        string? answer = null;
        var reason = refusal.Reason;

        if (refusal.Answer is { } given)
            try
            {
                answer = DecisionJson.Write(given);
            }
            catch (NotSupportedException e)
            {
                reason = $"{reason}; the answer itself could not be recorded: {e.Message}";
            }

        var record = new RecordedDecision
        {
            MetadataId = metadataId,
            QuestionKey = refusal.Question.Key,
            Occurrence = refusal.Occurrence,
            Fingerprint = refusal.Fingerprint,
            Kind = DecisionJson.Kind(refusal.Question),
            Question = DecisionJson.Write(refusal.Question),
            Answer = answer,
            Model = refusal.Answer?.Model,
            Decider = refusal.Decider.FullName,
            Refused = reason,
            DecidedAt = DateTime.UtcNow,
        };

        await Write(
            refusal.Train,
            refusal.RunId,
            async context =>
            {
                context.RecordedDecisions.Add(record);
                await context.SaveChanges(cancellationToken);
            },
            cancellationToken
        );
    }

    /// <summary>The answer for the log, which must not fail on an answer that cannot be recorded.</summary>
    private static string Describe(Answer answer)
    {
        try
        {
            return DecisionJson.Write(answer);
        }
        catch (NotSupportedException)
        {
            return answer.GetType().Name;
        }
    }

    private static RecordedDecision Record(DecisionMade decision, long metadataId) =>
        new()
        {
            MetadataId = metadataId,
            QuestionKey = decision.Question.Key,
            Occurrence = decision.Occurrence,
            Fingerprint = decision.Fingerprint,
            Kind = DecisionJson.Kind(decision.Question),
            Question = DecisionJson.Write(decision.Question),
            Answer = DecisionJson.Write(decision.Answer, decision.ReplayRefused),
            Model = decision.Answer.Model,
            Decider = decision.Decider?.FullName,
            Replayed = decision.Replayed,
            Shadows = DecisionJson.Write(decision.Shadows),
            DecidedAt = DateTime.UtcNow,
        };

    /// <inheritdoc />
    public async Task Routed(TrackRouted routing, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Train {Train} (run {RunId}) took track {Track} on {On}{Fallback}",
            routing.Train,
            routing.RunId,
            routing.Track,
            QuestionKey.For(routing.On),
            routing.FallbackReason is null ? "" : $" because {routing.FallbackReason}"
        );

        // The routing is added to the row of the latest decision it routes on.
        if (
            await Bound(routing.Train, routing.RunId, replay: false, cancellationToken)
            is not { MetadataId: { } metadataId } run
        )
            return;

        var key = QuestionKey.For(routing.On);

        long recordId;

        // A decision written while the run's flow was lost is in the table but not in Latest.
        if (run.Latest.TryGetValue(key, out var latest))
            recordId = latest;
        else if (await LatestRecorded(routing, metadataId, key, cancellationToken) is { } written)
            recordId = written;
        else
            return;

        await Write(
            routing.Train,
            routing.RunId,
            async context =>
            {
                var record = await context.RecordedDecisions.FirstOrDefaultAsync(
                    d => d.Id == recordId,
                    cancellationToken
                );

                // Gone only if its run was deleted meanwhile, which leaves nothing to route.
                if (record is null)
                    return;

                // Added to what earlier steps routed on the same decision, never in place of it.
                record.Routes = DecisionJson.AddRoute(
                    record.Routes,
                    routing.Track,
                    routing.FallbackReason
                );
                await context.SaveChanges(cancellationToken);
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answered from what <c>ServiceTrain.Run</c> loaded before the run's first junction, so it
    /// never goes to the database on the train's path.
    /// </remarks>
    public async Task<RecordedAnswer?> Replay(
        string train,
        string runId,
        string key,
        int occurrence,
        CancellationToken cancellationToken
    ) =>
        (await Bound(train, runId, replay: true, cancellationToken))?.Replay.GetValueOrDefault(
            (key, occurrence)
        );

    /// <summary>
    /// Binds the run to its row and loads the answers of the runs it replays. A run that names one
    /// that cannot be replayed (it, or a run it replays in turn, does not exist, is a run of another
    /// train, ran without recording its decisions, or has an answer that cannot be read, or the
    /// runs lead back on themselves or further than <see cref="MaxReplayChain"/>) fails here,
    /// classified permanent; one whose answers cannot be loaded because the database failed fails
    /// classified transient. A run that recorded its decisions but reached no questions is
    /// replayed like any other: there is nothing to repeat, and its questions are asked afresh.
    /// </summary>
    async Task<DecisionRun> IDecisionRunRecorder.Begin(
        Metadata metadata,
        Type train,
        CancellationToken cancellationToken
    )
    {
        // A run that was never persisted has no row to write against; its decisions are logged.
        long? metadataId = metadata.Id > 0 ? metadata.Id : null;

        var replay = metadata.ReplayDecisionsOf is { } source
            ? await LoadReplay(
                new Replaying(metadata.Id, metadata.Name, metadata.ExternalId, source),
                cancellationToken
            )
            : NothingToReplay;

        var run = new DecisionRun(metadata.ExternalId, train, metadataId, replay);

        if (metadataId is not null)
            _rowNames.GetOrAdd(run.Train, _ => new()).TryAdd(metadata.Name, 0);

        return run;
    }

    /// <summary>The run whose replay is loaded, and the run it names.</summary>
    private sealed record Replaying(long Id, string Name, string ExternalId, long Source);

    /// <summary>
    /// How many runs back a replay follows <c>replay_decisions_of</c>. A run requeued this many
    /// times over is failed rather than followed further.
    /// </summary>
    public const int MaxReplayChain = 32;

    /// <summary>
    /// The answers a run replays: those of the run it names, and, for a question that run never
    /// reached, those of the run that one replayed, and so on back. A requeue of a requeue that
    /// failed before it reached a question still takes the track the first run took there.
    /// </summary>
    private async Task<IReadOnlyDictionary<(string, int), RecordedAnswer>> LoadReplay(
        Replaying metadata,
        CancellationToken cancellationToken
    )
    {
        var source = metadata.Source;

        var chain = new List<long>();
        string? broken = null;
        List<(
            long MetadataId,
            string Key,
            int Occurrence,
            string Fingerprint,
            string? Answer
        )> recorded;

        try
        {
            using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            var seen = new HashSet<long> { metadata.Id };
            long? next = source;
            var replayedBy = metadata.Id;

            while (next is { } id)
            {
                if (!seen.Add(id))
                {
                    broken = $"the runs it replays lead back to run {id}";
                    break;
                }

                if (chain.Count == MaxReplayChain)
                {
                    broken = $"the runs it replays go back more than {MaxReplayChain} runs";
                    break;
                }

                var link = await context
                    .Metadatas.AsNoTracking()
                    .Where(m => m.Id == id)
                    .Select(m => new
                    {
                        m.Name,
                        m.ReplayDecisionsOf,
                        m.DecisionsRecorded,
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (link is null)
                {
                    broken =
                        id == source
                            ? $"no run {id} exists"
                            : $"run {id}, whose decisions run {replayedBy} replays, no longer exists";
                    break;
                }

                // Another train's answers were given to other questions in another chain; a
                // matching key or fingerprint would only make them look like this train's.
                if (link.Name != metadata.Name)
                {
                    broken = $"run {id} is a run of train '{link.Name}', not of this train";
                    break;
                }

                // A run that did not record its decisions may have acted on answers nobody can
                // know now. One that replays an earlier run made none of its own, because a run
                // that names a run to replay fails before its first junction where decisions are
                // not recorded, so the replay goes on to the run it named. One that does not was
                // the first, and what it decided is lost.
                if (!link.DecisionsRecorded && link.ReplayDecisionsOf is null)
                {
                    broken =
                        $"run {id} ran without recording its decisions, so what it decided "
                        + "cannot be known";
                    break;
                }

                chain.Add(id);
                replayedBy = id;
                next = link.ReplayDecisionsOf;
            }

            recorded = broken is not null ? [] : (
                    await context
                        .RecordedDecisions.AsNoTracking()
                        // A refused answer was never acted on, so there is nothing of it to repeat.
                        .Where(d => chain.Contains(d.MetadataId) && d.Refused == null)
                        .Select(d => new
                        {
                            d.MetadataId,
                            d.QuestionKey,
                            d.Occurrence,
                            d.Fingerprint,
                            d.Answer,
                        })
                        .ToListAsync(cancellationToken)
                ).Select(d => (d.MetadataId, d.QuestionKey, d.Occurrence, d.Fingerprint, d.Answer)).ToList();
        }
        catch (Exception e)
            when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                e,
                "Could not load the decisions of run {Source} for run {RunId} to replay.",
                source,
                metadata.ExternalId
            );

            DecisionRun.Classified(e, metadata.Name, metadata.ExternalId, FailureClass.Transient);
            throw;
        }

        if (broken is not null)
            throw DecisionRun.Unreplayable(metadata.Name, metadata.ExternalId, source, broken);

        var answers = new Dictionary<(string, int), RecordedAnswer>();
        var nearestFirst = chain.Select((id, depth) => (id, depth)).ToDictionary();

        // The nearer run's answer wins: it is what that run acted on, whether it replayed it or
        // was answered afresh because the older one no longer fitted.
        foreach (
            var (runId, key, occurrence, fingerprint, answer) in recorded.OrderBy(d =>
                nearestFirst[d.MetadataId]
            )
        )
        {
            if (answers.ContainsKey((key, occurrence)))
                continue;

            try
            {
                answers[(key, occurrence)] = new RecordedAnswer(
                    // A row with neither an answer nor a refusal is damaged, and read as such.
                    DecisionJson.ReadAnswer(answer ?? "null"),
                    fingerprint
                );
            }
            catch (JsonException e)
            {
                throw DecisionRun.Unreplayable(
                    metadata.Name,
                    metadata.ExternalId,
                    source,
                    $"the answer run {runId} recorded to '{key}' cannot be read: {e.Message}"
                );
            }
        }

        return answers;
    }

    /// <summary>
    /// Writes through a short-lived context of its own, so the write neither flushes what the
    /// run's junctions have tracked nor leaves a failed entity tracked in the run's context.
    /// </summary>
    private async Task Write(
        string train,
        string runId,
        Func<Services.DataContext.IDataContext, Task> write,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await write(context);
        }
        catch (StoreRefusedContentException e)
        {
            // The same decision is refused the same way on every retry.
            DecisionRun.Classified(e, train, runId, FailureClass.Permanent);
            throw;
        }
    }

    /// <summary>
    /// The run Trax.Core reported: the one on this flow, or, when the flow carries no run, the one
    /// found by its row. Null outside a service train's run, or for a decision of another train run
    /// on the same flow (a plain train a junction runs), whose decisions are logged only.
    /// </summary>
    /// <param name="train">The train as Trax.Core named it.</param>
    /// <param name="runId">The external id Trax.Core reported.</param>
    /// <param name="replay">Whether a run found by its row needs the answers it replays.</param>
    /// <param name="cancellationToken">The run's token.</param>
    /// <exception cref="Trax.Core.Exceptions.TrainException">
    /// The run's own train reported the decision under another external id. Writing nothing and
    /// carrying on would leave a decision the run acted on out of the record its requeue replays.
    /// </exception>
    private async Task<DecisionRun?> Bound(
        string train,
        string runId,
        bool replay,
        CancellationToken cancellationToken
    ) =>
        DecisionRun.Current switch
        {
            { } run when run.RunId == runId => run,
            { } run when run.Train == train => throw run.Unbound(runId),
            { } => null,
            null => await Lost(train, runId, replay, cancellationToken),
        };

    /// <summary>
    /// The run a decision reported with no run on its flow belongs to: the one run of
    /// <paramref name="train"/> in progress under <paramref name="runId"/>, among the rows that
    /// train has begun recorded runs under on this host, or null when there is not exactly one.
    /// </summary>
    /// <remarks>
    /// A train that never began a recorded run here, a plain train run inside a junction above
    /// all, is never looked up, so its decisions cannot be taken for a run's even when it shares
    /// the run's external id. The lookup uses <c>ix_metadata_external_id</c>. A database that
    /// cannot be reached fails it, classified transient, as a write would.
    /// </remarks>
    private async Task<DecisionRun?> Lost(
        string train,
        string runId,
        bool replay,
        CancellationToken cancellationToken
    )
    {
        if (!_rowNames.TryGetValue(train, out var known) || known.IsEmpty)
            return null;

        var names = known.Keys.ToList();
        List<(long Id, string Name, long? ReplayDecisionsOf)> runs;

        try
        {
            using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            runs = (
                await context
                    .Metadatas.AsNoTracking()
                    .Where(m =>
                        m.ExternalId == runId
                        && m.TrainState == TrainState.InProgress
                        && m.DecisionsRecorded
                        && names.Contains(m.Name)
                    )
                    .Select(m => new
                    {
                        m.Id,
                        m.Name,
                        m.ReplayDecisionsOf,
                    })
                    .Take(2)
                    .ToListAsync(cancellationToken)
            ).Select(m => (m.Id, m.Name, m.ReplayDecisionsOf)).ToList();
        }
        catch (Exception e)
            when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            DecisionRun.Classified(e, train, runId, FailureClass.Transient);
            throw;
        }

        if (runs.Count != 1)
        {
            if (runs.Count > 1)
                _logger.LogWarning(
                    "Train {Train} reported a decision for run {RunId} on an async flow that does "
                        + "not carry its run, and more than one run of it is in progress under "
                        + "that external id, so it cannot be told which; it is logged only.",
                    train,
                    runId
                );

            return null;
        }

        var (id, name, source) = runs[0];

        _logger.LogWarning(
            "Train {Train} reported a decision for run {RunId} on an async flow that does not carry "
                + "its run, as happens when code in the run suppresses ExecutionContext flow. It is "
                + "recorded against run {MetadataId}, the one run of it in progress under that "
                + "external id.",
            train,
            runId,
            id
        );

        var answers =
            replay && source is { } replayed
                ? await LoadReplay(new Replaying(id, name, runId, replayed), cancellationToken)
                : NothingToReplay;

        return new DecisionRun(runId, train, id, answers);
    }

    /// <summary>
    /// The row written for the latest asking of <paramref name="key"/> in the run, for a routing
    /// whose decision was written while the run's flow was lost, or null when there is none.
    /// </summary>
    private async Task<long?> LatestRecorded(
        TrackRouted routing,
        long metadataId,
        string key,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            return await context
                .RecordedDecisions.AsNoTracking()
                .Where(d => d.MetadataId == metadataId && d.QuestionKey == key && d.Refused == null)
                .OrderByDescending(d => d.Occurrence)
                .ThenByDescending(d => d.Id)
                .Select(d => (long?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception e)
            when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            DecisionRun.Classified(e, routing.Train, routing.RunId, FailureClass.Transient);
            throw;
        }
    }
}
