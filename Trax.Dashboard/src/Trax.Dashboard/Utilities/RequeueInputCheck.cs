using System.Text.Json;
using Trax.Effect.Utils;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Whether a run's saved input can be re-queued as the input it ran with. The re-queue reads the
/// saved JSON back as the train's input type, so anything that is not that input (nothing saved,
/// a placeholder the parameter effect wrote instead, or an input with masked members) would run
/// the train with defaults in place of the real values. The rule and the messages are the API's
/// <c>requeueExecution</c> ones, so the dashboard and the API refuse the same runs.
/// </summary>
internal static class RequeueInputCheck
{
    /// <summary>
    /// The markers the parameter effect writes, as <c>{"marker": true, ...}</c>, in place of an
    /// input it could not record: one over <c>MaxParameterBytes</c>, one that failed to serialize,
    /// and one holding a disposed <c>JsonDocument</c>.
    /// </summary>
    private static readonly string[] PlaceholderMarkers =
    [
        "_truncated",
        "_unserializable",
        "_disposed",
    ];

    /// <summary>
    /// Why the run's saved input cannot be re-queued, or <see langword="null"/> when it can.
    /// </summary>
    /// <param name="metadataId">The run's id, named in the message.</param>
    /// <param name="input">The run's saved input JSON.</param>
    public static string? RefusalFor(long metadataId, string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return $"Execution {metadataId} has no saved input to re-queue it with. Inputs are "
                + "saved only when SaveTrainParameters() is on.";

        switch (SavedPlaceholder(input))
        {
            case "_truncated":
                return $"Execution {metadataId}'s input was too large to save in full, so it "
                    + "cannot be re-queued with what it ran with.";
            case { } marker:
                return $"Execution {metadataId}'s input could not be saved (it was recorded as a "
                    + $"{marker} placeholder), so it cannot be re-queued with what it ran with.";
        }

        if (TraxRedaction.ContainsRedaction(input))
            return $"Execution {metadataId}'s input has values masked by [TraxSensitive], so it "
                + "cannot be re-queued with what it ran with.";

        return null;
    }

    /// <summary>
    /// The placeholder marker <paramref name="input"/> carries at its root with the value
    /// <see langword="true"/>, or <see langword="null"/> when it is a recorded input (an input
    /// whose own member happens to share a marker's name, with any other value, is one).
    /// </summary>
    internal static string? SavedPlaceholder(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var marker in PlaceholderMarkers)
                if (
                    document.RootElement.TryGetProperty(marker, out var flag)
                    && flag.ValueKind == JsonValueKind.True
                )
                    return marker;

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
