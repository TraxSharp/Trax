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
using Trax.Effect.Services.JunctionEvents;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;

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
/// <param name="contextFactory">Creates the short-lived data contexts the journal reads and writes through.</param>
/// <param name="logger">Optional; without one, decisions are recorded but not logged.</param>
/// <param name="options">
/// How decisions are recorded and replayed: the options <c>AddDecisionRecording</c> registers, which
/// the container passes however the journal is registered. The defaults when none is given.
/// </param>
public sealed class DecisionJournal(
    IDataContextProviderFactory contextFactory,
    ILogger<DecisionJournal>? logger = null,
    DecisionRecordingOptions? options = null
) : IDecisionObserver, IDecisionReplay, IDecisionRunRecorder
{
    /// <summary>
    /// A journal with the default recording options, as if <c>AddDecisionRecording</c> had been
    /// called with no configuration.
    /// </summary>
    /// <param name="contextFactory">Creates the short-lived data contexts the journal reads and writes through.</param>
    /// <param name="logger">Optional; without one, decisions are recorded but not logged.</param>
    public DecisionJournal(
        IDataContextProviderFactory contextFactory,
        ILogger<DecisionJournal>? logger
    )
        : this(contextFactory, logger, null) { }

    /// <summary>How decisions are recorded and replayed, as <c>AddDecisionRecording</c> configured them.</summary>
    internal DecisionRecordingOptions Options { get; } = options ?? new();

    // Optional, so a host that registers no logging still records decisions instead of failing
    // every run that makes one.
    private readonly ILogger _logger = logger ?? NullLogger<DecisionJournal>.Instance;

    private static readonly IReadOnlyDictionary<
        (string, string, int),
        RecordedAnswer
    > NothingToReplay = new Dictionary<(string, string, int), RecordedAnswer>();

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
        var sensitive = SensitiveQuestions.IsSensitive(
            decision.QuestionType,
            decision.Question.Key
        );

        // The run is found first, because a decision on a withheld track is logged without its
        // question, answer and decider. A run that cannot be found is logged as on one.
        DecisionRun? run;
        try
        {
            run = await Bound(decision.Train, decision.RunId, replay: false, cancellationToken);
        }
        catch
        {
            LogDecided(decision, sensitive, onWithheldTrack: true);
            throw;
        }

        LogDecided(decision, sensitive, run?.OnWithheldTrack ?? false);

        if (run is not { MetadataId: { } metadataId })
            return;

        RecordedDecision record;

        try
        {
            record = Record(decision, metadataId, StateHashToRecord(decision));
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

        run.Decided(record.QuestionKey, record.Id);
    }

    private void LogDecided(DecisionMade decision, bool sensitive, bool onWithheldTrack)
    {
        if (onWithheldTrack)
        {
            _logger.LogInformation(
                "Train {Train} (run {RunId}) decided {Question}: {Answer} by {Decider}{Replayed}",
                decision.Train,
                decision.RunId,
                WithheldTrack,
                WithheldTrack,
                WithheldTrack,
                decision.Replayed ? " (replayed)" : ""
            );
            return;
        }

        _logger.LogInformation(
            "Train {Train} (run {RunId}) decided {Question}: {Answer} by {Decider}{Replayed}{Shadows}{Refused}",
            decision.Train,
            decision.RunId,
            decision.Question.Key,
            sensitive ? Withheld : Describe(decision.Answer),
            decision.Decider?.Name ?? "replay",
            decision.Replayed ? " (replayed)" : "",
            decision.Shadows.Count == 0
                ? ""
                : $"; shadows agreeing: {decision.Shadows.Count(s => s.Agrees)}/{decision.Shadows.Count}",
                // Why a recorded answer was not replayed can describe that answer.
                decision.ReplayRefused
                    is null
                    ? ""
                : sensitive ? $"; not replayed: {Withheld}"
                : $"; not replayed: {decision.ReplayRefused}"
        );
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
        var sensitive = SensitiveQuestions.IsSensitive(refusal.QuestionType, refusal.Question.Key);

        DecisionRun? run;
        try
        {
            run = await Bound(refusal.Train, refusal.RunId, replay: false, cancellationToken);
        }
        catch
        {
            LogRefused(refusal, sensitive, onWithheldTrack: true);
            throw;
        }

        LogRefused(refusal, sensitive, run?.OnWithheldTrack ?? false);

        if (run is not { MetadataId: { } metadataId })
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
            BranchPath = BranchPaths.Current,
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

    private void LogRefused(DecisionRefused refusal, bool sensitive, bool onWithheldTrack) =>
        _logger.LogWarning(
            "Train {Train} (run {RunId}) refused the answer to {Question}: {Answer} by {Decider}, because {Reason}",
            refusal.Train,
            refusal.RunId,
            onWithheldTrack ? WithheldTrack : refusal.Question.Key,
            onWithheldTrack ? WithheldTrack
                : sensitive ? Withheld
                : refusal.Answer is null ? "no answer"
                : Describe(refusal.Answer),
            onWithheldTrack ? WithheldTrack : refusal.Decider.Name,
                // The reason quotes the answer.
                onWithheldTrack ? WithheldTrack
                : sensitive ? Withheld
                : refusal.Reason
        );

    /// <summary>
    /// What the log says in place of an answer to a question about a type marked
    /// <c>[TraxSensitive]</c>. The row keeps the answer, because a requeue replays it from there.
    /// </summary>
    private const string Withheld = "(withheld: the question is marked [TraxSensitive])";

    /// <summary>
    /// What the log says in place of the question, answer, track and decider of a decision or
    /// routing on a track whose answer is withheld, which would give that track away. The row
    /// keeps them, because a requeue replays from there.
    /// </summary>
    private const string WithheldTrack =
        "(withheld: the run is on a track whose answer is withheld)";

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

    /// <summary>The state types (or trains) a warning about an unkeyed hash was logged for.</summary>
    private readonly ConcurrentDictionary<string, byte> _unkeyedWarned = new();

    /// <summary>
    /// The state hash to record with the answer: Trax.Core's, unless it is unkeyed and the state
    /// can hold a value marked <c>[TraxSensitive]</c> (or the question is about a type so marked),
    /// in which case none, so the answer is never replayed. A keyed hash is always recorded.
    /// </summary>
    /// <remarks>
    /// An unkeyed hash can be computed by anyone, so for a state whose values are kept out of
    /// every other record it is not stored either. A warning saying to configure a key is logged
    /// once per state type. A decision reported without its state type is treated the same way.
    /// </remarks>
    private string? StateHashToRecord(DecisionMade decision)
    {
        if (decision.StateHash is not { } hash || hash.StartsWith("k1:", StringComparison.Ordinal))
            return decision.StateHash;

        var sensitive =
            decision.StateType is not { } state
            || TraxRedaction.ReachesSensitiveMember(state)
            || SensitiveQuestions.IsSensitive(decision.QuestionType, decision.Question.Key);

        if (!sensitive)
            return hash;

        var about = decision.StateType?.FullName ?? decision.Train;
        if (_unkeyedWarned.TryAdd(about, 0))
            _logger.LogWarning(
                "Answers to questions about {State} are recorded without the hash of their state, "
                    + "so they are never replayed: the state can hold a value marked "
                    + "[TraxSensitive], and no state hash key is configured. Configure one with "
                    + "AddDecisionRecording(o => o.HashStatesWith(key)) or {ConfigurationKey}.",
                about,
                DecisionRecordingOptions.StateHashKeyConfigurationKey
            );

        return null;
    }

    private static RecordedDecision Record(
        DecisionMade decision,
        long metadataId,
        string? stateHash
    ) =>
        new()
        {
            MetadataId = metadataId,
            BranchPath = BranchPaths.Current,
            QuestionKey = decision.Question.Key,
            Occurrence = decision.Occurrence,
            Fingerprint = decision.Fingerprint,
            Kind = DecisionJson.Kind(decision.Question),
            Question = DecisionJson.Write(decision.Question),
            Answer = DecisionJson.Write(decision.Answer, decision.ReplayRefused),
            Model = decision.Answer.Model,
            Decider = decision.Decider?.FullName,
            Replayed = decision.Replayed,
            StateHash = stateHash,
            Shadows = DecisionJson.Write(decision.Shadows),
            DecidedAt = DateTime.UtcNow,
        };

    /// <inheritdoc />
    public async Task Routed(TrackRouted routing, CancellationToken cancellationToken)
    {
        var sensitive = SensitiveQuestions.IsSensitive(routing.On);

        DecisionRun? run;
        try
        {
            run = await Bound(routing.Train, routing.RunId, replay: false, cancellationToken);
        }
        catch
        {
            LogRouted(routing, sensitive, onWithheldTrack: true);
            throw;
        }

        LogRouted(routing, sensitive, run?.OnWithheldTrack ?? false);

        // Every later decision of the run is on this track.
        if (sensitive && run is not null)
            run.OnWithheldTrack = true;

        // The routing is added to the row of the latest decision it routes on.
        if (run is not { MetadataId: { } metadataId })
            return;

        var key = QuestionKey.For(routing.On);

        long recordId;

        // A decision written while the run's flow was lost is in the table but not in Latest.
        if (run.TryLatest(key, out var latest))
            recordId = latest;
        else if (await LatestRecorded(routing, metadataId, key, cancellationToken) is { } written)
            recordId = written;
        else
            return;

        // Branches that route on one decision made before they forked add to the same row.
        await run.Routing.WaitAsync(cancellationToken);
        try
        {
            await AddRoute(routing, recordId, cancellationToken);
        }
        finally
        {
            run.Routing.Release();
        }
    }

    private Task AddRoute(
        TrackRouted routing,
        long recordId,
        CancellationToken cancellationToken
    ) =>
        Write(
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

    private void LogRouted(TrackRouted routing, bool sensitive, bool onWithheldTrack) =>
        _logger.LogInformation(
            "Train {Train} (run {RunId}) took track {Track} on {On}{Fallback}",
            routing.Train,
            routing.RunId,
            onWithheldTrack ? WithheldTrack
                : sensitive ? Withheld
                : routing.Track,
            onWithheldTrack ? WithheldTrack : QuestionKey.For(routing.On),
            routing.FallbackReason is null || sensitive || onWithheldTrack
                ? ""
                : $" because {routing.FallbackReason}"
        );

    /// <inheritdoc />
    /// <remarks>
    /// Answered from what <c>ServiceTrain.Run</c> loaded before the run's first junction, so it
    /// never goes to the database on the train's path. Inside a <c>Parallel</c> branch the answer
    /// is the one recorded by the same branch, because branches count their askings on from the
    /// fork and two of them asking one question ask it under the same occurrence.
    /// </remarks>
    public async Task<RecordedAnswer?> Replay(
        string train,
        string runId,
        string key,
        int occurrence,
        CancellationToken cancellationToken
    ) =>
        (await Bound(train, runId, replay: true, cancellationToken))?.Replay.GetValueOrDefault(
            (BranchPaths.Current, key, occurrence)
        );

    /// <summary>
    /// Binds the run to its row and loads the answers of the runs it replays. A run that names one
    /// that cannot be replayed (it, or a run it replays in turn, does not exist, is a run of another
    /// train, ran without recording its decisions, or has an answer that cannot be read, or the
    /// runs lead back on themselves or further than <see cref="MaxReplayChain"/>) fails here,
    /// classified permanent, unless it is a manifest's retry, which asks afresh with a warning and
    /// is marked <see cref="Metadata.ReplayAbandoned"/>; one whose answers cannot be loaded because
    /// the database failed fails classified transient. The chain stops at a run marked
    /// <see cref="Metadata.ReplayAbandoned"/>. A run that recorded its decisions but reached no questions is
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

        var replay = NothingToReplay;

        if (metadata.ReplayDecisionsOf is { } source)
        {
            (replay, var abandoned) = await LoadReplay(
                new Replaying(
                    metadata.Id,
                    metadata.Name,
                    metadata.ExternalId,
                    source,
                    metadata.ManifestId is not null
                ),
                cancellationToken
            );

            // Set on the run's own row too, so the outcome it writes keeps it.
            if (abandoned)
                metadata.ReplayAbandoned = true;
        }

        var run = new DecisionRun(metadata.ExternalId, train, metadataId, replay);

        if (metadataId is not null)
            _rowNames.GetOrAdd(run.Train, _ => new()).TryAdd(metadata.Name, 0);

        return run;
    }

    /// <summary>One recorded answer a replay may use.</summary>
    private sealed record Recorded(
        long MetadataId,
        string BranchPath,
        string Key,
        int Occurrence,
        string Fingerprint,
        string? Answer,
        string? StateHash,
        bool Replayed,
        DateTime DecidedAt
    );

    /// <summary>
    /// When a decider gave the answer the nearest run acted on: that row's time when it was asked
    /// afresh, otherwise the time of the nearest row further back that was. A replay takes the
    /// nearest answer in its chain, so the next row back for the same asking is the one a replayed
    /// row repeated. Null when the chain ends before reaching one.
    /// </summary>
    private static DateTime? AnsweredAt(IEnumerable<Recorded> nearestFirst) =>
        nearestFirst.FirstOrDefault(row => !row.Replayed)?.DecidedAt;

    /// <summary>The run whose replay is loaded, and the run it names.</summary>
    /// <param name="Id">The run's row.</param>
    /// <param name="Name">The run's train name.</param>
    /// <param name="ExternalId">The run's external id.</param>
    /// <param name="Source">The run it names to replay.</param>
    /// <param name="Retry">
    /// True for a run of a manifest, whose replay is a retry the scheduler queued: one that cannot be
    /// honoured asks afresh, with a warning, instead of failing the retry.
    /// </param>
    private sealed record Replaying(
        long Id,
        string Name,
        string ExternalId,
        long Source,
        bool Retry
    );

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
    /// <returns>
    /// The answers, and whether the run abandoned its replay: a manifest's retry whose chain is
    /// broken asks afresh, and that is written to its row at once
    /// (<see cref="Metadata.ReplayAbandoned"/>), so a run that dies mid-way is marked too.
    /// </returns>
    private async Task<(
        IReadOnlyDictionary<(string, string, int), RecordedAnswer> Answers,
        bool Abandoned
    )> LoadReplay(Replaying metadata, CancellationToken cancellationToken)
    {
        var source = metadata.Source;

        var chain = new List<long>();
        string? broken = null;
        List<Recorded> recorded;

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
                        m.ReplayAbandoned,
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

                // A run that abandoned its replay asked afresh, so it never acted on the answers
                // of the run it named. One that never started, or failed before its first
                // junction, carries no mark and is passed through as before.
                var abandoned = link.ReplayDecisionsOf is not null && link.ReplayAbandoned;

                // A run that did not record its decisions may have acted on answers nobody can
                // know now. One that replays an earlier run made none of its own, because a run
                // that names a run to replay fails before its first junction where decisions are
                // not recorded, unless it abandoned that replay, so the replay goes on to the run
                // it named. One that does not was the first, and what it decided is lost.
                if (!link.DecisionsRecorded && (link.ReplayDecisionsOf is null || abandoned))
                {
                    broken =
                        $"run {id} ran without recording its decisions, so what it decided "
                        + "cannot be known";
                    break;
                }

                chain.Add(id);
                replayedBy = id;

                // A recording run that abandoned its replay answered every question it reached
                // itself, so the chain ends with it.
                next = abandoned ? null : link.ReplayDecisionsOf;
            }

            recorded = broken is not null ? [] : (
                    await context
                        .RecordedDecisions.AsNoTracking()
                        // A refused answer was never acted on, so there is nothing of it to repeat.
                        .Where(d => chain.Contains(d.MetadataId) && d.Refused == null)
                        .Select(d => new
                        {
                            d.MetadataId,
                            d.BranchPath,
                            d.QuestionKey,
                            d.Occurrence,
                            d.Fingerprint,
                            d.Answer,
                            d.StateHash,
                            d.Replayed,
                            d.DecidedAt,
                        })
                        .ToListAsync(cancellationToken)
                ).Select(d => new Recorded(d.MetadataId, d.BranchPath, d.QuestionKey, d.Occurrence, d.Fingerprint, d.Answer, d.StateHash, d.Replayed, d.DecidedAt)).ToList();
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
        {
            if (!metadata.Retry)
                throw DecisionRun.Unreplayable(metadata.Name, metadata.ExternalId, source, broken);

            _logger.LogWarning(
                "Retry {RunId} of train {Train} asks its questions afresh instead of replaying the "
                    + "decisions of run {Source}, because {Reason}.",
                metadata.ExternalId,
                metadata.Name,
                source,
                broken
            );
            await MarkAbandoned(metadata, cancellationToken);
            return (NothingToReplay, true);
        }

        var answers = new Dictionary<(string, string, int), RecordedAnswer>();
        var nearestFirst = chain.Select((id, depth) => (id, depth)).ToDictionary();

        var byAsking = recorded
            .OrderBy(d => nearestFirst[d.MetadataId])
            .GroupBy(d => (d.BranchPath, d.Key, d.Occurrence))
            .ToList();
        // A bound longer than the time since DateTime.MinValue means no bound at all.
        var now = DateTime.UtcNow;
        var oldest =
            Options.MaxReplayAge >= now - DateTime.MinValue
                ? DateTime.MinValue
                : now - Options.MaxReplayAge;

        // The nearer run's answer wins: it is what that run acted on, whether it replayed it or
        // was answered afresh because the older one no longer fitted.
        foreach (var asking in byAsking)
        {
            var nearest = asking.First();
            var (runId, branchPath, key, occurrence, fingerprint, answer, stateHash, _, _) =
                nearest;

            // Its age is that of the answer a decider gave, not of a later run replaying it, so a
            // chain of requeues cannot keep an answer alive. One whose answering run is gone is
            // as old as can be.
            if (AnsweredAt(asking) is not { } answeredAt || answeredAt < oldest)
            {
                _logger.LogInformation(
                    "Run {RunId} asks '{Question}' (occurrence {Occurrence}) afresh instead of "
                        + "replaying the answer run {Source} acted on: it was given longer ago "
                        + "than answers are replayed for ({MaxReplayAge}).",
                    metadata.ExternalId,
                    key,
                    occurrence,
                    runId,
                    Options.MaxReplayAge
                );
                continue;
            }

            try
            {
                answers[(branchPath, key, occurrence)] = new RecordedAnswer(
                    // A row with neither an answer nor a refusal is damaged, and read as such.
                    DecisionJson.ReadAnswer(answer ?? "null"),
                    fingerprint
                )
                {
                    StateHash = stateHash,
                };
            }
            catch (JsonException e)
            {
                var why = $"the answer run {runId} recorded to '{key}' cannot be read: {e.Message}";

                if (!metadata.Retry)
                    throw DecisionRun.Unreplayable(metadata.Name, metadata.ExternalId, source, why);

                _logger.LogWarning(
                    "Retry {RunId} of train {Train} asks '{Question}' afresh, because {Reason}.",
                    metadata.ExternalId,
                    metadata.Name,
                    key,
                    why
                );
            }
        }

        return (answers, false);
    }

    /// <summary>
    /// Marks the run's row as having abandoned its replay, at once, so a later replay of it stops
    /// there even if the run never writes its outcome. A failure is logged: the run carries on, and
    /// its outcome writes the mark when it is written.
    /// </summary>
    private async Task MarkAbandoned(Replaying metadata, CancellationToken cancellationToken)
    {
        if (metadata.Id <= 0)
            return;

        try
        {
            using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var row = await context.Metadatas.FirstOrDefaultAsync(
                m => m.Id == metadata.Id,
                cancellationToken
            );
            if (row is null || row.ReplayAbandoned)
                return;

            row.ReplayAbandoned = true;
            await context.SaveChanges(cancellationToken);
        }
        catch (Exception e)
            when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                e,
                "Could not mark retry {RunId} of train {Train} as asking afresh; its outcome marks it.",
                metadata.ExternalId,
                metadata.Name
            );
        }
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
        List<(long Id, string Name, long? ReplayDecisionsOf, bool Retry)> runs;

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
                        m.ManifestId,
                    })
                    .Take(2)
                    .ToListAsync(cancellationToken)
            ).Select(m => (m.Id, m.Name, m.ReplayDecisionsOf, m.ManifestId is not null)).ToList();
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

        var (id, name, source, retry) = runs[0];

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
                ? (
                    await LoadReplay(
                        new Replaying(id, name, runId, replayed, retry),
                        cancellationToken
                    )
                ).Answers
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
        // Read here, on the routing step's flow. As DecisionRun.TryLatest does, a branch routes on
        // its own asking or, before it asked, on that of the nearest chain enclosing it, such as a
        // decision made before the fork; never a sibling's.
        var enclosing = BranchPaths.Enclosing(BranchPaths.Current);

        try
        {
            using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            var candidates = await context
                .RecordedDecisions.AsNoTracking()
                .Where(d =>
                    d.MetadataId == metadataId
                    && enclosing.Contains(d.BranchPath)
                    && d.QuestionKey == key
                    && d.Refused == null
                )
                .Select(d => new
                {
                    d.Id,
                    d.BranchPath,
                    d.Occurrence,
                })
                .ToListAsync(cancellationToken);

            return candidates
                .OrderByDescending(d => d.BranchPath.Length)
                .ThenByDescending(d => d.Occurrence)
                .ThenByDescending(d => d.Id)
                .Select(d => (long?)d.Id)
                .FirstOrDefault();
        }
        catch (Exception e)
            when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            DecisionRun.Classified(e, routing.Train, routing.RunId, FailureClass.Transient);
            throw;
        }
    }
}
