using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>
/// A CLR enum spelt like the state type the schema generates for <see cref="LedgerMachine"/>, as Recovery's
/// <c>TopicMapState</c> is for the topic map. A train's output exposes it.
/// </summary>
public enum LedgerState
{
    Open,
    Reconciled,
    Archived,
}
