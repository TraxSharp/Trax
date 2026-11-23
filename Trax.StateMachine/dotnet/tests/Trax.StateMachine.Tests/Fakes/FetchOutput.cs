using Trax.Effect.Services.ServiceTrain;

// The ingest train's contract has a namespace of its own, which the Trax.Cli tests declare identically, so both
// suites describe the same machine and export the same committed ingest.ir.json.
namespace Ingest.Contracts;

/// <summary>A pointer to what the train fetched, never the data: a fingerprint, and whether it is sure.</summary>
public sealed record FetchOutput
{
    public string Fingerprint { get; init; } = "";
    public bool Unsure { get; init; }
}
