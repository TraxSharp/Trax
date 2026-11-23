using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>A system-owned machine from its IR: only the server creates and moves its instances.</summary>
internal sealed class SystemIrMachine<TState, TTrigger>(IrReplay replay)
    : IrServedMachine<TState, TTrigger>(replay)
    where TState : struct, Enum
    where TTrigger : struct, Enum;
