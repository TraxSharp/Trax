// What the example on sdk-reference/client-api/dotnet-client.md takes as given: two documents and their records, as
// trax client --operations --lang csharp generates them from the console's DisableManifest and OnTrainStateChanged.
using System.Text.Json.Serialization;
using Trax.Client;

public sealed record DisableManifestVariables(
    [property: JsonPropertyName("externalId")] string ExternalId
);

public sealed record DisableManifestResult(
    [property: JsonPropertyName("operations")] DisableManifestOperations Operations
);

public sealed record DisableManifestOperations(
    [property: JsonPropertyName("disableManifest")] DisableManifestOutcome DisableManifest
);

public sealed record DisableManifestOutcome(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string? Message
);

public sealed record OnTrainStateChangedResult(
    [property: JsonPropertyName("onTrainStateChanged")] TrainStateChange OnTrainStateChanged
);

public sealed record TrainStateChange(
    [property: JsonPropertyName("trainName")] string TrainName,
    [property: JsonPropertyName("trainState")] string TrainState
);

public static class Documents
{
    public static readonly TraxDocument<
        DisableManifestVariables,
        DisableManifestResult
    > DisableManifest = new(
        "DisableManifest",
        TraxOperationKind.Mutation,
        "trax_console_DisableManifest_0000",
        "mutation DisableManifest($externalId: String!) { operations { disableManifest(externalId: $externalId) { success message } } }"
    );

    public static readonly TraxDocument<
        TraxNoVariables,
        OnTrainStateChangedResult
    > OnTrainStateChanged = new(
        "OnTrainStateChanged",
        TraxOperationKind.Subscription,
        "trax_console_OnTrainStateChanged_0000",
        "subscription OnTrainStateChanged { onTrainStateChanged { trainName trainState } }"
    );
}
