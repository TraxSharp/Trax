using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Core.Utils;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Whether a run can resume, and where: what an operator surface shows and what a resumed run
/// acts on.
/// </summary>
/// <param name="CanResume">True when the run can resume as asked.</param>
/// <param name="Code">Why it cannot, as a stable code (<c>no-checkpoint</c>, <c>missing-input</c>, <c>chain-changed</c> and the rest), or null.</param>
/// <param name="Reason">Why it cannot, phrased for an operator, or null.</param>
/// <param name="Target">The step the resumed run skips to, or null to run its main chain from the top.</param>
/// <param name="Checkpoint">The checkpoint its main chain restores, or null.</param>
public sealed record ResumeVerdict(
    bool CanResume,
    string? Code,
    string? Reason,
    string? Target,
    string? Checkpoint
);

/// <summary>
/// Decides whether a failed run can resume, from its checkpoints and its train's declared chain,
/// without running anything. The operator's resume and the scheduler's retries ask it before they
/// queue a resumed run; the resumed run asks again when it starts.
/// </summary>
/// <remarks>See Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</remarks>
public interface IRunResumes
{
    /// <summary>
    /// Whether the run <paramref name="runId"/> of <paramref name="train"/> can resume at
    /// <paramref name="resumeAt"/>, or after its latest checkpoint when that is null.
    /// </summary>
    /// <param name="train">The train's class.</param>
    /// <param name="chain">Its declared chain.</param>
    /// <param name="input">Its input type.</param>
    /// <param name="output">Its output type.</param>
    /// <param name="runId">The run to resume.</param>
    /// <param name="resumeAt">The step to resume at, or null.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<ResumeVerdict> Check(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        string? resumeAt,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Whether the run <paramref name="runId"/> of <paramref name="train"/> can resume after its
    /// latest checkpoint and at each of <paramref name="points"/>, from one read of its lineage,
    /// with the nodes that hold a checkpoint it can resume from and, when it is itself a resumed
    /// run, the nodes it restored rather than ran. What an operator's run graph shows; each verdict
    /// is the one <see cref="Check"/> gives for that point.
    /// </summary>
    /// <remarks>
    /// An implementation that predates it asks <see cref="Check"/> once per point and reports no
    /// checkpoints and no restored nodes.
    /// </remarks>
    /// <param name="train">The train's class.</param>
    /// <param name="chain">Its declared chain.</param>
    /// <param name="input">Its input type.</param>
    /// <param name="output">Its output type.</param>
    /// <param name="runId">The run.</param>
    /// <param name="points">The node ids to check a resume at.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    async Task<ResumeChecks> CheckMany(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        IReadOnlyCollection<string> points,
        CancellationToken cancellationToken
    )
    {
        var latest = await Check(train, chain, input, output, runId, null, cancellationToken)
            .ConfigureAwait(false);
        var at = new Dictionary<string, ResumeVerdict>(StringComparer.Ordinal);

        foreach (var point in points)
            at[point] = await Check(train, chain, input, output, runId, point, cancellationToken)
                .ConfigureAwait(false);

        return new ResumeChecks(latest, at, [], [], []);
    }
}

/// <summary>
/// A run's resume verdicts and checkpoints, as <see cref="IRunResumes.CheckMany"/> reads them. It
/// names nodes only: never a checkpoint's stored state or the tracks stored with it.
/// </summary>
/// <param name="Latest">Whether the run can resume after its latest checkpoint.</param>
/// <param name="At">Whether it can resume at each point asked about, by node id.</param>
/// <param name="Checkpoints">
/// The nodes holding a checkpoint the run can resume from: those it wrote, and for a resumed run,
/// those the runs it resumed wrote before the point it resumed at.
/// </param>
/// <param name="Written">The nodes at which the run itself wrote a checkpoint.</param>
/// <param name="Restored">
/// For a resumed run, the nodes before the point it resumed at, which it skipped and whose work
/// the checkpoint restored; empty for any other run, or when the resume can no longer be planned
/// against the running chain.
/// </param>
public sealed record ResumeChecks(
    ResumeVerdict Latest,
    IReadOnlyDictionary<string, ResumeVerdict> At,
    IReadOnlyCollection<string> Checkpoints,
    IReadOnlyCollection<string> Written,
    IReadOnlyCollection<string> Restored
)
{
    /// <summary>
    /// For a resumed run, the track each routing step before the point it resumed at took, by
    /// node id, as the checkpoint it restored stored it. The run recorded no step for those
    /// routing steps, so this is how the tracks it passed over show as skipped. A route the
    /// checkpoint withheld (its key is marked sensitive) is not here.
    /// </summary>
    public IReadOnlyDictionary<string, string> RestoredTracks { get; init; } =
        new Dictionary<string, string>();
}

/// <inheritdoc />
internal sealed class RunResumes(IEnumerable<ICheckpointRows> rows, IServiceProvider services)
    : IRunResumes
{
    public async Task<ResumeVerdict> Check(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        string? resumeAt,
        CancellationToken cancellationToken
    )
    {
        if (rows.FirstOrDefault() is not { } store)
            return ResumePlanner.NoRows;

        var lineage = await store.Lineage(runId, cancellationToken).ConfigureAwait(false);

        return ResumePlanner
            .Plan(
                chain,
                ChainGraph.From(chain, train, input, output).Hash,
                input,
                output,
                services.GetService<IServiceProviderIsService>(),
                lineage,
                resumeAt
            )
            .Verdict;
    }

    public async Task<ResumeChecks> CheckMany(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        IReadOnlyCollection<string> points,
        CancellationToken cancellationToken
    )
    {
        if (rows.FirstOrDefault() is not { } store)
            return new ResumeChecks(
                ResumePlanner.NoRows,
                points
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(p => p, _ => ResumePlanner.NoRows, StringComparer.Ordinal),
                [],
                [],
                []
            );

        // One lineage read for every point.
        var lineage = await store.Lineage(runId, cancellationToken).ConfigureAwait(false);
        var hash = ChainGraph.From(chain, train, input, output).Hash;
        var container = services.GetService<IServiceProviderIsService>();

        ResumeVerdict At(string? point) =>
            ResumePlanner.Plan(chain, hash, input, output, container, lineage, point).Verdict;

        return new ResumeChecks(
            At(null),
            points
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(p => p, p => At(p), StringComparer.Ordinal),
            ResumePlanner.Chosen(chain, lineage).Keys.ToList(),
            lineage.Count > 0 ? lineage[0].Rows.Select(r => r.NodeId).Distinct().ToList() : [],
            ResumePlanner.Restored(chain, input, output, container, lineage)
        )
        {
            RestoredTracks = ResumePlanner.RestoredTracks(chain, input, output, container, lineage),
        };
    }
}

/// <summary>
/// Builds a resume from a run's lineage: which checkpoints count, whether the chain allows the
/// resume, whether each restored checkpoint still matches the code, and the states read back.
/// </summary>
internal static class ResumePlanner
{
    internal static readonly ResumeVerdict NoRows = new(
        false,
        ResumeRefusals.NoCheckpoint,
        "This host stores no checkpoints, so a run can only run again from the top.",
        null,
        null
    );

    /// <summary>
    /// The verdict, and when it allows the resume, the plan a run acts on.
    /// </summary>
    /// <param name="chain">The train's declared chain.</param>
    /// <param name="chainHash">The running chain's hash.</param>
    /// <param name="input">The train's input type.</param>
    /// <param name="output">The train's output type.</param>
    /// <param name="container">Whether the container supplies a type, or null.</param>
    /// <param name="lineage">The run to resume and those it resumed, nearest first.</param>
    /// <param name="resumeAt">The step to resume at, or null for after the latest checkpoint.</param>
    public static (ResumeVerdict Verdict, ResumePlan? Plan) Plan(
        ChainRecorder chain,
        string chainHash,
        Type input,
        Type output,
        IServiceProviderIsService? container,
        IReadOnlyList<ResumedRun> lineage,
        string? resumeAt
    )
    {
        var chosen = Chosen(chain, lineage);

        var outcome = ChainVerification.CheckResume(
            chain,
            input,
            output,
            container is null ? null : container.IsService,
            chosen.Keys.ToList(),
            resumeAt
        );

        if (!outcome.CanResume)
            return (Refused(outcome.RefusalCode!, outcome.Refusal!), null);

        var restored = new Dictionary<string, RestoredCheckpoint>();
        var targets = new List<(string Key, string Node)>();
        if (outcome.MainCheckpoint is { } main)
            targets.Add(("", main));
        targets.AddRange(outcome.BranchCheckpoints.Select(b => (b.Key, b.Value)));

        foreach (var (key, node) in targets)
        {
            var row = chosen[node];
            var stateType = outcome.StateTypes[node];

            if (
                ChainVerification.CheckStored(
                    node,
                    row.ChainHash,
                    chainHash,
                    row.StateFingerprint,
                    stateType
                ) is
                { } changed
            )
                return (Refused(changed.RefusalCode!, changed.Refusal!), null);

            object state;
            IReadOnlyList<object> tracks;

            try
            {
                state =
                    JsonSerializer.Deserialize(row.State, stateType)
                    ?? throw new JsonException("the stored state is null");
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

        return (
            new ResumeVerdict(true, null, null, outcome.Target, outcome.MainCheckpoint),
            new ResumePlan(outcome.Target, outcome.Inclusive, restored)
        );
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
    /// The nodes the first run of <paramref name="lineage"/> skipped because it resumed: every
    /// node before the point it resumed at, the point too when it is the checkpoint it restored,
    /// and in a branch that resumed from its own checkpoint, the branch's nodes up to that
    /// checkpoint. Planned as the run planned it when it started; empty for a run that did not
    /// resume, or whose resume the running chain no longer allows.
    /// </summary>
    internal static IReadOnlyCollection<string> Restored(
        ChainRecorder chain,
        Type input,
        Type output,
        IServiceProviderIsService? container,
        IReadOnlyList<ResumedRun> lineage
    )
    {
        if (lineage.Count < 2 || lineage[0].ResumeFrom is null)
            return [];

        var outcome = ChainVerification.CheckResume(
            chain,
            input,
            output,
            container is null ? null : container.IsService,
            Chosen(chain, lineage.Skip(1).ToList()).Keys.ToList(),
            lineage[0].ResumeAt
        );

        if (!outcome.CanResume)
            return [];

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

        return restored;
    }

    /// <summary>
    /// The track each routing step before a resumed run's point took, read from the routes the
    /// checkpoint it restored stored: a routing step's id names the key it routes on
    /// (<c>Switch&lt;Source&gt;#0</c>), and each stored route names its key's type.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> RestoredTracks(
        ChainRecorder chain,
        Type input,
        Type output,
        IServiceProviderIsService? container,
        IReadOnlyList<ResumedRun> lineage
    )
    {
        var tracks = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lineage.Count < 2 || lineage[0].ResumeFrom is null)
            return tracks;

        var chosen = Chosen(chain, lineage.Skip(1).ToList());
        var outcome = ChainVerification.CheckResume(
            chain,
            input,
            output,
            container is null ? null : container.IsService,
            chosen.Keys.ToList(),
            lineage[0].ResumeAt
        );

        if (!outcome.CanResume || outcome.MainCheckpoint is not { } main)
            return tracks;

        var restored = Restored(chain, input, output, container, lineage)
            .ToHashSet(StringComparer.Ordinal);
        var stored = Tracks(chosen[main].Tracks, outcome.TrackTypes[main]);

        foreach (var taken in stored)
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

        return tracks;
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
            var type = declared.FirstOrDefault(t => t.GetGenericArguments()[0].FullName == key);

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
        new(false, code, reason, null, null);
}
