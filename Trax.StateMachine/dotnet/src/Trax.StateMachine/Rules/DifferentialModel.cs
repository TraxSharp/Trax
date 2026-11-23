using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// The differential fuzzing inputs authored on a machine (test-only) with <c>.Differential(...)</c>:
/// representative per-trigger input <see cref="Samples"/>, per-state <see cref="Seeds"/> contexts (BFS start
/// points, one or more per state), and dense probe <see cref="Contexts"/> (crossed with every state). The IR exporter emits these so
/// the cross-language differential harness enumerates off the one C# source, with no hand-written machine.json.
/// The harness always adds a no-input case per trigger, so an explicit empty (<c>{}</c>) sample is distinct.
/// </summary>
public sealed record DifferentialModel<TState, TTrigger>(
    IReadOnlyDictionary<TTrigger, IReadOnlyList<JsonNode>> Samples,
    IReadOnlyDictionary<TState, IReadOnlyList<JsonNode>> Seeds,
    IReadOnlyList<JsonNode> Contexts
)
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>
    /// True when no samples, seeds or probe contexts were authored. The IR exporter omits the
    /// <c>differential</c> section for an empty model, so the IR matches a machine without one.
    /// </summary>
    public bool IsEmpty =>
        Samples.Count == 0 && Seeds.Count == 0 && Contexts.Count == 0 && OutcomeSamples.Count == 0;

    /// <summary>
    /// Representative outputs per invoking state, fired as that state's <c>done</c> outcome trigger. Exported under
    /// <c>samples</c> keyed by the outcome trigger (<c>Fetching.done</c>).
    /// </summary>
    internal IReadOnlyDictionary<TState, IReadOnlyList<JsonNode>> OutcomeSamples { get; init; } =
        new Dictionary<TState, IReadOnlyList<JsonNode>>();
}
