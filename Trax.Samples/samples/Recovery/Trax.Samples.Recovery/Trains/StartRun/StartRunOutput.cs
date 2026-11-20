using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.StartRun;

public record StartRunOutput
{
    /// <summary>The run id the manifest's input carries, the key for the crash and the case file.</summary>
    public required string RunId { get; init; }

    /// <summary>The one-off manifest's database id: every attempt's execution carries it.</summary>
    public required long ManifestId { get; init; }

    public required string ManifestExternalId { get; init; }

    /// <summary>The canonical name of the train the manifest runs.</summary>
    public required string TrainName { get; init; }

    /// <summary>The crash the run meets first; a run armed paying then emailing names the payment.</summary>
    public required CrashPoint ArmedCrash { get; init; }

    /// <summary>How many times the manifest retries a failed run before dead-lettering it.</summary>
    public required int MaxRetries { get; init; }

    /// <summary>
    /// The external id of the manifest's first run, which the caller follows with runProgress; each retry is named
    /// by the run before it (<c>retriedAs</c>).
    /// </summary>
    public required string FirstRunId { get; init; }
}
