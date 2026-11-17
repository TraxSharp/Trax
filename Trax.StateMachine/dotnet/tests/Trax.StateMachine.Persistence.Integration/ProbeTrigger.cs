using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Testing;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

public enum ProbeTrigger
{
    Close,
}
