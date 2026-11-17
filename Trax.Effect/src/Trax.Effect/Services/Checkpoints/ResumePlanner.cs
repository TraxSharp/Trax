using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Core.Utils;
using Trax.Effect.Services.JunctionEvents;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Builds a resume from a run's lineage: which checkpoints count, whether the chain allows the
/// resume, whether each restored checkpoint still matches the code, and the states read back.
/// </summary>
/// <remarks>
/// A verdict is decided from the rows' nodes, chain hashes and state fingerprints alone; a row's
/// state is read, and read back once, only when a run is about to restore it.
/// </remarks>
internal static class ResumePlanner
{
    internal static readonly ResumeVerdict NoRows = new(
        CanResume: false,
        ResumeRefusals.NoCheckpoint,
        "This host stores no checkpoints, so a run can only run again from the top.",
        Target: null,
        Checkpoint: null
    );

    /// <summary>
    /// Whether the store withholds a route: a checkpoint keeps no route whose key is marked
    /// sensitive, so a resume restores none.
    /// </summary>
    internal static bool Withheld(Type key) => SensitiveQuestions.IsSensitive(key);

    /// <summary>
    /// The verdict on a resume at <paramref name="resumeAt"/>, and when it allows it, the outcome
    /// a plan is built from. Reads no state.
    /// </summary>
    /// <param name="chain">The train's declared chain.</param>
    /// <param name="chainHash">The running chain's hash.</param>
    /// <param name="input">The train's input type.</param>
    /// <param name="output">The train's output type.</param>
    /// <param name="container">Whether the container supplies a type, or null.</param>
    /// <param name="chosen">The checkpoints the lineage holds, as <see cref="Chosen"/> picks them.</param>
    /// <param name="resumeAt">The step to resume at, or null for after the latest checkpoint.</param>
    public static (ResumeVerdict Verdict, ResumeOutcome? Outcome) Check(
        ChainRecorder chain,
        string chainHash,
        Type input,
        Type output,
        IServiceProviderIsService? container,
        IReadOnlyDictionary<string, Models.Checkpoint.Checkpoint> chosen,
        string? resumeAt
    )
    {
        var outcome = ChainVerification.CheckResume(
            chain,
            input,
            output,
            container is null ? null : container.IsService,
            chosen.Keys.ToList(),
            resumeAt,
            Withheld
        );

        if (!outcome.CanResume)
            return (Refused(outcome.RefusalCode!, outcome.Refusal!), null);

        foreach (var (_, node) in Targets(outcome))
        {
            var row = chosen[node];

            if (
                ChainVerification.CheckStored(
                    node,
                    row.ChainHash,
                    chainHash,
                    row.StateFingerprint,
                    outcome.StateTypes[node]
                ) is
                { } changed
            )
                return (Refused(changed.RefusalCode!, changed.Refusal!), null);
        }

        return (
            new ResumeVerdict(
                CanResume: true,
                Code: null,
                Reason: null,
                outcome.Target,
                outcome.MainCheckpoint
            ),
            outcome
        );
    }

    /// <summary>
    /// The verdict, and when it allows the resume, the plan a run acts on: the states of the
    /// checkpoints it restores, read with <paramref name="states"/> and read back once each.
    /// </summary>
    /// <param name="chain">The train's declared chain.</param>
    /// <param name="chainHash">The running chain's hash.</param>
    /// <param name="input">The train's input type.</param>
    /// <param name="output">The train's output type.</param>
    /// <param name="container">Whether the container supplies a type, or null.</param>
    /// <param name="lineage">The run to resume and those it resumed, nearest first.</param>
    /// <param name="resumeAt">The step to resume at, or null for after the latest checkpoint.</param>
    /// <param name="states">Reads the stored states of the rows with the given ids.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<(ResumeVerdict Verdict, ResumePlan? Plan)> Plan(
        ChainRecorder chain,
        string chainHash,
        Type input,
        Type output,
        IServiceProviderIsService? container,
        IReadOnlyList<ResumedRun> lineage,
        string? resumeAt,
        Func<
            IReadOnlyCollection<long>,
            CancellationToken,
            Task<IReadOnlyDictionary<long, string>>
        > states,
        CancellationToken cancellationToken
    )
    {
        var chosen = Chosen(chain, lineage);
        var (verdict, outcome) = Check(
            chain,
            chainHash,
            input,
            output,
            container,
            chosen,
            resumeAt
        );

        if (outcome is null)
            return (verdict, null);

        var targets = Targets(outcome);
        var stored = await states(
                targets.Select(t => chosen[t.Node].Id).Distinct().ToList(),
                cancellationToken
            )
            .ConfigureAwait(false);
        var restored = new Dictionary<string, RestoredCheckpoint>();

        foreach (var (key, node) in targets)
        {
            var row = chosen[node];
            var stateType = outcome.StateTypes[node];
            object state;
            IReadOnlyList<object> tracks;

            try
            {
                state =
                    JsonSerializer.Deserialize(
                        stored.GetValueOrDefault(row.Id)
                            ?? throw new JsonException("the stored state is gone"),
                        stateType
                    ) ?? throw new JsonException("the stored state is null");
                tracks = Tracks(row.Tracks, outcome.TrackTypes[node]);
            }
            catch (JsonException e)
            {
                return (
                    Refused(
                        "unreadable",
                        $"The checkpoint '{node}' could not be read back as "
                            + $"'{stateType.FullName}': {e.Message}"
                    ),
                    null
                );
            }

            restored[key] = new RestoredCheckpoint(node, stateType, state, tracks);
        }

        return (verdict, new ResumePlan(outcome.Target, outcome.Inclusive, restored));
    }

    /// <summary>The checkpoints an outcome restores: the main chain's under the empty key, then each branch's.</summary>
    private static List<(string Key, string Node)> Targets(ResumeOutcome outcome)
    {
        var targets = new List<(string Key, string Node)>();
        if (outcome.MainCheckpoint is { } main)
            targets.Add(("", main));
        targets.AddRange(outcome.BranchCheckpoints.Select(b => (b.Key, b.Value)));
        return targets;
    }

    /// <summary>
    /// The checkpoints a resume of the first run of <paramref name="lineage"/> can restore, by
    /// node id. A resumed run wrote only the checkpoints after its own point, so the run it resumed
    /// counts for what came before that point. The nearest run's row wins for one node.
    /// </summary>
    internal static Dictionary<string, Models.Checkpoint.Checkpoint> Chosen(
        ChainRecorder chain,
        IReadOnlyList<ResumedRun> lineage
    )
    {
        var order = Order(chain);
        var chosen = new Dictionary<string, Models.Checkpoint.Checkpoint>();
        var limit = int.MaxValue;

        foreach (var run in lineage)
        {
            foreach (var row in run.Rows)
                if (order.TryGetValue(row.NodeId, out var at) && at < limit)
                    chosen.TryAdd(row.NodeId, row);

            if (run.ResumeAt is { } point && order.TryGetValue(point, out var resumedAt))
                limit = Math.Min(limit, resumedAt);
        }

        return chosen;
    }

    /// <summary>
    /// What the first run of <paramref name="lineage"/> skipped because it resumed, from one plan
    /// of its resume as it planned it when it started; both empty for a run that did not resume,
    /// or whose resume the running chain no longer allows.
    /// </summary>
    /// <returns>
    /// The nodes it skipped (every node before the point it resumed at, the point too when it is
    /// the checkpoint it restored, and in a branch that resumed from its own checkpoint, the
    /// branch's nodes up to that checkpoint), and the track each routing step among them took,
    /// read from the routes the checkpoint it restored stored: a routing step's id names the key
    /// it routes on (<c>Switch&lt;Source&gt;#0</c>), and each stored route names its key's type.
    /// </returns>
    internal static (
        IReadOnlyCollection<string> Restored,
        IReadOnlyDictionary<string, string> Tracks
    ) Resumed(
        ChainRecorder chain,
        Type input,
        Type output,
        IServiceProviderIsService? container,
        IReadOnlyList<ResumedRun> lineage
    )
    {
        var tracks = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lineage.Count < 2 || lineage[0].ResumeFrom is null)
            return ([], tracks);

        var chosen = Chosen(chain, lineage.Skip(1).ToList());
        var outcome = ChainVerification.CheckResume(
            chain,
            input,
            output,
            container is null ? null : container.IsService,
            chosen.Keys.ToList(),
            lineage[0].ResumeAt,
            Withheld
        );

        if (!outcome.CanResume)
            return ([], tracks);

        var order = Order(chain);
        var target = outcome.Target is { } t && order.TryGetValue(t, out var at) ? at : -1;
        var restored = new List<string>();

        foreach (var (id, index) in order)
        {
            var before = index < target || (index == target && outcome.Inclusive);
            var inRestoredBranch = outcome.BranchCheckpoints.Any(b =>
                id.StartsWith(b.Key + "/", StringComparison.Ordinal)
                && order.TryGetValue(b.Value, out var checkpoint)
                && index <= checkpoint
            );

            if (before || inRestoredBranch)
                restored.Add(id);
        }

        if (outcome.MainCheckpoint is not { } main)
            return (restored, tracks);

        foreach (var taken in Tracks(chosen[main].Tracks, outcome.TrackTypes[main]))
        {
            var key = taken.GetType().GetGenericArguments()[0].ReadableName();
            var name = (string)taken.GetType().GetProperty("Track")!.GetValue(taken)!;

            foreach (var id in restored)
            {
                var step = id[(id.LastIndexOf('/') + 1)..];
                if (
                    step.StartsWith($"Switch<{key}>#", StringComparison.Ordinal)
                    || step.StartsWith($"Gate<{key}>#", StringComparison.Ordinal)
                    || step.StartsWith($"Scale<{key}>#", StringComparison.Ordinal)
                )
                    tracks[id] = name;
            }
        }

        return (restored, tracks);
    }

    private static Dictionary<string, int> Order(ChainRecorder chain) =>
        ChainVerification
            .NodeOrder(chain)
            .Select((id, i) => (id, i))
            .ToDictionary(n => n.id, n => n.i);

    /// <summary>
    /// The stored routes read back as the <c>TrackTaken</c> values the chain declares. A route
    /// whose key the chain no longer routes on is left out.
    /// </summary>
    private static List<object> Tracks(string json, IReadOnlyList<Type> declared)
    {
        var tracks = new List<object>();

        foreach (var node in JsonNode.Parse(json)?.AsArray() ?? [])
        {
            var key = node?["key"]?.GetValue<string>();
            var type = declared.FirstOrDefault(t =>
                string.Equals(t.GetGenericArguments()[0].FullName, key, StringComparison.Ordinal)
            );

            if (type is null)
                continue;

            tracks.Add(
                Activator.CreateInstance(
                    type,
                    node!["track"]?.GetValue<string>(),
                    node["fallbackReason"]?.GetValue<string>()
                )!
            );
        }

        return tracks;
    }

    private static ResumeVerdict Refused(string code, string reason) =>
        new(CanResume: false, code, reason, Target: null, Checkpoint: null);
}
