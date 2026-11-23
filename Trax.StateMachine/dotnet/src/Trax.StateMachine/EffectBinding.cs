using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// One irreversible effect bound to a transition, declared inline on the machine (never wired in DI by
/// hand). The persistence layer resolves <see cref="EffectType"/> from the container and runs it
/// exactly-once when the transition fires.
/// </summary>
public sealed record EffectBinding<TState, TTrigger>(
    TState From,
    TTrigger Trigger,
    TState To,
    Type EffectType,
    string KeyPrefix
)
    where TState : struct, Enum
    where TTrigger : struct, Enum;
