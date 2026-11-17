using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// What a view field shows a person (ADR <c>Trax.Docs/adr/0058</c>): read from that person's own instances of a user
/// machine the view reads, or from the system instance, never from anyone else's. A state shows its label when the view
/// gives one. A scenario's step and the replay read it the same way.
/// </summary>
internal static class ViewValues
{
    internal static IReadOnlyList<string> Shown(
        GlossaryView view,
        string field,
        string person,
        IEnumerable<(InstanceKey Key, string? State, JsonObject Context)> instances
    )
    {
        var shown = view.Fields.First(f => string.Equals(f.Name, field, StringComparison.Ordinal));
        return instances
            .Where(i =>
                string.Equals(i.Key.Machine, shown.Machine, StringComparison.Ordinal)
                && (
                    i.Key.Owner is null
                    || string.Equals(i.Key.Owner, person, StringComparison.Ordinal)
                )
                && i.State is not null
            )
            .Select(i =>
                shown.IsState
                    ? shown.Labels?.GetValueOrDefault(i.State!) ?? i.State!
                    : Text(i.Context[shown.Source])
            )
            .ToList();
    }

    private static string Text(JsonNode? value) =>
        value switch
        {
            null => "",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => value.ToJsonString(),
        };
}
