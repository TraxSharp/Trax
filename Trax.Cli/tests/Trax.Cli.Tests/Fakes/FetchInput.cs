// The train's contract is declared as in Trax.StateMachine.Tests, so both describe the same machine and
// export the one committed ingest.ir.json.
namespace Ingest.Contracts;

public sealed record FetchInput(string Source);
