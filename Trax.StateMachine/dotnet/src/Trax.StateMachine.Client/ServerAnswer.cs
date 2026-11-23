using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>What the server answered a start, an event or a read with: a snapshot or a refusal, never both.</summary>
public sealed record ServerAnswer
{
    /// <summary>The instance's id, on a start's answer.</summary>
    public string? Id { get; init; }

    /// <summary>What the server stored, when it did not refuse.</summary>
    public ServerSnapshot? Snapshot { get; init; }

    /// <summary>The refusal, when it refused.</summary>
    public ServerProblem? Problem { get; init; }

    /// <summary>An answer that carries a snapshot.</summary>
    public static ServerAnswer Of(ServerSnapshot snapshot, string? id = null) =>
        new() { Snapshot = snapshot, Id = id };

    /// <summary>An answer that refuses.</summary>
    public static ServerAnswer Refused(string code, string message) =>
        new() { Problem = new ServerProblem(code, message) };
}
