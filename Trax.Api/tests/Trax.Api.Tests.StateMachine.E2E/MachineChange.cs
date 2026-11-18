using System.Text.Json;
using Trax.Core.Extensions;

namespace Trax.Api.StateMachine.E2E;

/// <summary>One push event.</summary>
public sealed record MachineChange(JsonElement Raw)
{
    /// <summary>The revision of the snapshot, or 0 when the event carries none.</summary>
    public long Revision =>
        Raw.TryGetProperty("revision", out var revision)
            ? revision.ValueKind switch
            {
                JsonValueKind.Number => revision.GetInt64(),
                JsonValueKind.String => long.Parse(
                    revision.GetString().NotNull(),
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                _ => 0,
            }
            : 0;

    /// <summary>The state, as the schema spells it.</summary>
    public string? State =>
        Raw.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString()
            : null;

    /// <summary>Whether the instance is gone, which ends the stream.</summary>
    public bool Gone =>
        Raw.TryGetProperty("gone", out var gone) && gone.ValueKind == JsonValueKind.True;

    /// <summary>Whether the client must read the instance again.</summary>
    public bool Reload =>
        Raw.TryGetProperty("reload", out var reload) && reload.ValueKind == JsonValueKind.True;

    /// <summary>The problem's code, or null.</summary>
    public string? ProblemCode =>
        Raw.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object
            ? problem.GetProperty("code").GetString()
            : null;

    /// <summary>Whether the state is <paramref name="state"/>, however the schema spells an enum value.</summary>
    public bool StateIs(string state) => MachineResult.SameName(State, state);
}
