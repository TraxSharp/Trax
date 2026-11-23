using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// The result of migrating a snapshot one version forward: the (possibly transformed) state token and
/// context for the next version.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/migrations">SDK reference</seealso>
public sealed record MigrationResult(string State, JsonObject Context);
