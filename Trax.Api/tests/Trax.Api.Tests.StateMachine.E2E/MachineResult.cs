using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Core.Extensions;

namespace Trax.Api.StateMachine.E2E;

/// <summary>One answer of a typed machine mutation: the server's snapshot, or the problem it refused with.</summary>
public sealed record MachineResult(JsonElement Raw)
{
    /// <summary>The instance's id.</summary>
    public Guid Id =>
        Raw.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? Guid.Parse(id.GetString().NotNull())
            : Guid.Empty;

    /// <summary>The revision the server recorded, or null when the answer carries none.</summary>
    public long? Revision =>
        Raw.TryGetProperty("revision", out var revision)
            ? revision.ValueKind switch
            {
                JsonValueKind.Number => revision.GetInt64(),
                JsonValueKind.String => long.Parse(
                    revision.GetString().NotNull(),
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                _ => null,
            }
            : null;

    /// <summary>The state, as the schema spells it.</summary>
    public string? State =>
        Raw.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString()
            : null;

    /// <summary>The context object.</summary>
    public JsonElement Context => Raw.GetProperty("context");

    /// <summary>The refusal's code, or null when the server accepted.</summary>
    public string? ProblemCode =>
        Raw.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object
            ? problem.GetProperty("code").GetString()
            : null;

    /// <summary>The refusal's message, or null when the server accepted.</summary>
    public string? ProblemMessage =>
        Raw.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.Object
            ? problem.GetProperty("message").GetString()
            : null;

    /// <summary>
    /// Whether the state is <paramref name="state"/>, however the schema spells an enum value
    /// (<c>BuildFailed</c> or <c>BUILD_FAILED</c>).
    /// </summary>
    public bool StateIs(WizardState state) => SameName(State, state.ToString());

    /// <summary>Compares two enum spellings, ignoring case and underscores.</summary>
    public static bool SameName(string? a, string b) =>
        a is not null
        && string.Equals(
            a.Replace("_", "", StringComparison.Ordinal),
            b.Replace("_", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase
        );
}
