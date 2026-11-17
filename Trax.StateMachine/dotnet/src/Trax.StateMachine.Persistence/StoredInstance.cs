using Trax.Effect.Enums;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>A row read by its invoke token: who owns it, which draft it is, and the draft as stored.</summary>
/// <param name="Owner">The row's owner.</param>
/// <param name="Machine">The machine the row belongs to.</param>
/// <param name="Id">The draft or instance id.</param>
/// <param name="Snapshot">The stored snapshot, its concurrency token and its invoke token.</param>
internal sealed record StoredInstance(
    DraftOwner Owner,
    string Machine,
    Guid Id,
    StoredSnapshot Snapshot
);
