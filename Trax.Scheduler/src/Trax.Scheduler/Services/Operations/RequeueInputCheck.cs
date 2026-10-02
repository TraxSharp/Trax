using System.Text.Json;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Whether a run's saved input can be re-queued as the input it ran with. The re-queue reads the
/// saved JSON back as the train's input type, so anything that is not that input (nothing saved,
/// a placeholder the parameter effect wrote instead, or an input with masked members) would run
/// the train with defaults in place of the real values.
/// <see cref="OperationsService.RequeueExecutionAsync"/> applies it for both the GraphQL
/// <c>requeueExecution</c> mutation and the dashboard's Re-queue button, so the two refuse the
/// same runs with the same messages, and it holds the wording of every refusal particular to a
/// re-queue.
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
    /// The refusal for a run whose train this host no longer registers. The caller named a run,
    /// not a train, so it is not told to look the train's name up.
    /// </summary>
    public static string TrainNoLongerRegistered(long metadataId, string trainName) =>
        $"Train {trainName} is no longer registered, so execution {metadataId} cannot be re-queued.";

    /// <summary>
    /// The refusal for a run whose saved input no longer reads as its train's input type, which
    /// happens when the type changed shape after the run. The caller of a re-queue supplied no
    /// JSON, so the message names the run's saved input rather than an <c>InputJson</c>. It is
    /// given only once the mediator has authorized the caller, as an enqueue's parse error is.
    /// </summary>
    public static string SavedInputNoLongerReads(
        long metadataId,
        TrainRegistration registration,
        JsonException exception
    ) =>
        $"The saved input of run {metadataId} no longer reads as "
        + $"{registration.InputType.FullName}: {exception.Message}";

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
