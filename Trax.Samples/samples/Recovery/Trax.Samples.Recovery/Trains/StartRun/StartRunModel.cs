using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.StartRun;

public enum Scenario
{
    /// <summary>The refund: a switch, a gate and a checkpoint; a payment or an email that times out.</summary>
    Refund,

    /// <summary>The topic map: three signals in parallel branches, a branch that crashes.</summary>
    TopicMap,
}

public record StartRunInput
{
    public required Scenario Scenario { get; init; }

    /// <summary>The order to refund, from <c>CaseFiles.Catalog</c>; A-1001 when left out. Refund only.</summary>
    public string? OrderId { get; init; }

    /// <summary>The fields to map; every field in the corpus when left out. Topic map only.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>The first year to map; 2016 when left out. Topic map only.</summary>
    public int? FromYear { get; init; }

    /// <summary>The last year to map; 2025 when left out. Topic map only.</summary>
    public int? ToYear { get; init; }

    /// <summary>Crash the scenario's crash point once, on the first attempt.</summary>
    public bool CrashOnce { get; init; }

    /// <summary>Where a refund crashes when <see cref="CrashOnce"/> is set; while paying when left out.</summary>
    public RefundCrash? CrashWhile { get; init; }
}

/// <summary>Where a refund's crash fires: before its checkpoint or after it.</summary>
public enum RefundCrash
{
    /// <summary>In the step the approval routes to (paying, declining or queueing), before the checkpoint.</summary>
    Paying,

    /// <summary>In <c>NotifyCustomer</c>, after the checkpoint.</summary>
    Emailing,
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

    /// <summary>
    /// The external id of the manifest's first run, which the caller follows with runProgress; each retry is named
    /// by the run before it (<c>retriedAs</c>).
    /// </summary>
    public required string FirstRunId { get; init; }
}
