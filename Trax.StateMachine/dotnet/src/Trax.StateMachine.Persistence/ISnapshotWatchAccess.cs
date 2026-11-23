using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// A draft service that can tell a the push (<c>&lt;machine&gt;Changed</c>) subscription which row it may follow. Every service
/// <see cref="ISnapshotMachineRegistry"/> builds implements it; a subscription to any other is refused.
/// </summary>
internal interface ISnapshotWatchAccess
{
    /// <summary>
    /// The row id of the caller's instance <paramref name="id"/> when the machine's watch rule admits the caller, or
    /// null for every other case, alike.
    /// </summary>
    Task<long?> AuthorizeWatch(Actor actor, Guid id, CancellationToken cancellationToken = default);
}
