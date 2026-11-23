using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>A user-owned machine from its IR, served to clients as a <see cref="TraxMachineAttribute"/> machine is.</summary>
[TraxMachine]
internal sealed class ServedIrMachine<TState, TTrigger>(IrReplay replay)
    : IrServedMachine<TState, TTrigger>(replay)
    where TState : struct, Enum
    where TTrigger : struct, Enum;
