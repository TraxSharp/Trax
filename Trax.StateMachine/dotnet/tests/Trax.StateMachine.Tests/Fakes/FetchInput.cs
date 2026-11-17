using Trax.Effect.Services.ServiceTrain;

// The ingest train's contract has a namespace of its own, which the Trax.Cli tests declare identically, so both
// suites describe the same machine and export the same committed ingest.ir.json.
namespace Ingest.Contracts;

public sealed record FetchInput(string Source);
