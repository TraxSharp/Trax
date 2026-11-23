using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>One exposed train, as <see cref="ExposedTrains.Resolve"/> finds it.</summary>
/// <param name="Registration">The train.</param>
/// <param name="Root"><c>mutation</c> or <c>query</c>.</param>
/// <param name="Namespace">The namespace's field, or null.</param>
/// <param name="Field">The train's field.</param>
/// <param name="Holder">The name of the type that holds the field (<c>DispatchMutations</c>, a namespace's type).</param>
/// <param name="Definition">The field.</param>
internal sealed record ExposedTrain(
    TrainRegistration Registration,
    string Root,
    string? Namespace,
    string Field,
    string Holder,
    IOutputFieldDefinition Definition
);
