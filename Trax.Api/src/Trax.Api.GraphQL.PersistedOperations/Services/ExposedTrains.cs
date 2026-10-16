using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Where each exposed train is served: its root, namespace and field, the type that holds the field, and the field.
/// </summary>
internal static class ExposedTrains
{
    private const string MutationRootField = "dispatch";
    private const string QueryRootField = "discover";

    /// <summary>
    /// Every train in <paramref name="names"/> exposed with <c>[TraxMutation]</c> or <c>[TraxQuery]</c> that the
    /// schema serves, ordered by root, namespace and field.
    /// </summary>
    public static IReadOnlyList<ExposedTrain> Resolve(
        ISchemaDefinition schema,
        IReadOnlyDictionary<TrainRegistration, string> names
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(names);

        var trains = new List<ExposedTrain>();
        foreach (var (registration, trainName) in names)
        {
            if (!registration.IsMutation && !registration.IsQuery)
                continue;
            var root = registration.IsMutation ? "mutation" : "query";
            var ns = registration.GraphQLNamespace is { } n ? TrainTypeModule.CamelCase(n) : null;
            var field = TrainTypeModule.CamelCase(trainName);
            if (FieldOf(schema, registration.IsMutation, ns, field) is not var (holder, definition))
                continue;
            trains.Add(new ExposedTrain(registration, root, ns, field, holder, definition));
        }
        return trains
            .OrderBy(t => t.Root, StringComparer.Ordinal)
            .ThenBy(t => t.Namespace ?? "", StringComparer.Ordinal)
            .ThenBy(t => t.Field, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The field under <c>dispatch</c> or <c>discover</c>, inside <paramref name="ns"/> when it is set, and the type holding it.</summary>
    internal static (string Holder, IOutputFieldDefinition Definition)? FieldOf(
        ISchemaDefinition schema,
        bool mutation,
        string? ns,
        string field
    )
    {
        var root = mutation ? schema.MutationType : schema.QueryType;
        if (
            root is null
            || !root.Fields.TryGetField(
                mutation ? MutationRootField : QueryRootField,
                out var parent
            )
            || parent.Type.NamedType() is not IObjectTypeDefinition parentType
        )
            return null;
        var holder = parentType;
        if (ns is not null)
        {
            if (
                !parentType.Fields.TryGetField(ns, out var nsField)
                || nsField.Type.NamedType() is not IObjectTypeDefinition nsType
            )
                return null;
            holder = nsType;
        }
        return holder.Fields.TryGetField(field, out var definition)
            ? (holder.Name, definition)
            : null;
    }
}

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
