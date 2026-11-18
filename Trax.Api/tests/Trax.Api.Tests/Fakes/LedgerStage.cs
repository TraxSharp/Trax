using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>The machine's own CLR state enum, named so the generated state type cannot be its name.</summary>
public enum LedgerStage
{
    Drafted,
    Posted,
}
