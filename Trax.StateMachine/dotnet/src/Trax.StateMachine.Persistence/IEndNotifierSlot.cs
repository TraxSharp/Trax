using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// A draft service of a machine another machine's state may spawn, which the registry hands what delivers the end of
/// an instance to the parent waiting on it.
/// </summary>
internal interface IEndNotifierSlot
{
    /// <summary>The states the machine ends in; an advance into one of them calls <see cref="EndNotifier"/>.</summary>
    IReadOnlySet<string>? EndStates { get; set; }

    /// <summary>Delivers the end of the user's instance to its parent, when one waits on it. Never throws.</summary>
    Func<string, Guid, CancellationToken, Task>? EndNotifier { get; set; }
}
