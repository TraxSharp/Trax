using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.StartRun;

public enum Scenario
{
    /// <summary>The research brief: a switch and a scale, a report step that crashes.</summary>
    Research,

    /// <summary>The refund approval: a gate, a payment that times out.</summary>
    Refund,
}

public record StartRunInput
{
    public required Scenario Scenario { get; init; }

    /// <summary>The research topic; "What the papers say about cold-weather battery wear" when left out. Ignored for a refund.</summary>
    public string? Topic { get; init; }

    /// <summary>The order to refund, from <c>CaseFiles.Catalog</c>; A-1001 when left out. Ignored for research.</summary>
    public string? OrderId { get; init; }

    /// <summary>Crash the scenario's crash point once, on the first attempt.</summary>
    public bool CrashOnce { get; init; }
}

public record StartRunOutput
{
    /// <summary>The run id the manifest's input carries, the key for the crash and the case file.</summary>
    public required string RunId { get; init; }

    /// <summary>The one-off manifest's database id: every attempt's execution carries it.</summary>
    public required long ManifestId { get; init; }

    public required string ManifestExternalId { get; init; }

    /// <summary>The canonical name of the train the manifest runs.</summary>
    public required string TrainName { get; init; }

    public required CrashPoint ArmedCrash { get; init; }

    /// <summary>How many times the manifest retries a failed run before dead-lettering it.</summary>
    public required int MaxRetries { get; init; }
}
