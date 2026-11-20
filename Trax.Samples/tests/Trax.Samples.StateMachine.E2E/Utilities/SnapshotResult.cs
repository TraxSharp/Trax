using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>
/// An operation's answer: the snapshot as JSON, or the code of the problem that refused it. Exactly one of the two
/// is set.
/// </summary>
public sealed record SnapshotResult(string? Wire, string? ProblemCode, string Raw)
{
    /// <summary>The instance's id, when the answer carries one.</summary>
    public Guid? Id { get; init; }

    /// <summary>The snapshot, asserting that the operation succeeded.</summary>
    public JsonObject Snapshot
    {
        get
        {
            ProblemCode.Should().BeNull($"the operation should succeed: {Raw}");
            return JsonNode.Parse(Wire!)!.AsObject();
        }
    }

    public string State => Snapshot["state"]!.GetValue<string>();

    public JsonObject Context => Snapshot["context"]!.AsObject();
}
