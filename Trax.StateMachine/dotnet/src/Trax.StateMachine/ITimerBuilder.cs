using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// A state's timer: where the machine goes once the state has been held for the declared duration. See
/// <see cref="IStateBuilder{TState,TTrigger}.After(TimeSpan)"/>.
/// </summary>
public interface ITimerBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Finish the timer: land in <paramref name="target"/>. Returns the state builder for more configuration.</summary>
    IStateBuilder<TState, TTrigger> GoTo(TState target);
}
