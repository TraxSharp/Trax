using System.Reflection;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Attributes;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>The generic state-machine trains that carry a GraphQL exposure.</summary>
internal static class GenericMutationExposure
{
    /// <summary>Every generic state-machine train type that declares a <see cref="TraxMutationAttribute"/>.</summary>
    public static IReadOnlyList<Type> Exposed() =>
        new[]
        {
            typeof(SaveSnapshot),
            typeof(AdvanceSnapshot),
            typeof(LoadSnapshot),
            typeof(SendSnapshot),
            typeof(StartSnapshot),
            typeof(DiscardSnapshot),
        }
            .Where(type => Attribute.IsDefined(type, typeof(TraxMutationAttribute)))
            .ToList();
}
