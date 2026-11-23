using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>The machine a <see cref="ShelfMachine"/> built now has.</summary>
public enum ShelfShape
{
    /// <summary>The first version, which every host gets unless a test says otherwise.</summary>
    V1,

    /// <summary>The same machine with a timer added to <see cref="ShelfState.Filed"/>: a changed IR.</summary>
    V2WithTimer,

    /// <summary>
    /// The second version of the machine: the same states and events under <c>Version(2)</c>, with a migration from the
    /// first that keeps every instance as it is, a changed IR that needs no timer, as a deploy that changes the machine
    /// gives two nodes over one database.
    /// </summary>
    V2,

    /// <summary><see cref="ShelfTrigger.File"/> takes an input whose type holds itself.</summary>
    RecursiveInput,

    /// <summary>The context holds a type that holds itself.</summary>
    RecursiveContext,
}
