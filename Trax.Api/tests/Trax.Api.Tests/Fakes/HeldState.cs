using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

// The machines of the enum spelling tests: each holds one enum in its context, so the schema spells that enum as
// the server stores it.

public enum HeldState
{
    Open,
    Done,
}
