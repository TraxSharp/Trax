using HotChocolate;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.TypeModules;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// What a client document may select under <c>dispatch</c> and <c>discover</c>: the host's exposed trains and its
/// query models, each by root, namespace and field. Anything else the schema holds there (a hand-written
/// <c>[ExtendObjectType]</c> field, say) is not reachable from a client document. See
/// <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.
/// </summary>
internal sealed class ClientDocumentReach
{
    private readonly HashSet<(string Root, string Namespace, string Field)> _trains;
    private readonly HashSet<(string Namespace, string Field)> _queryModels;

    private ClientDocumentReach(
        HashSet<(string, string, string)> trains,
        HashSet<(string, string)> queryModels
    )
    {
        _trains = trains;
        _queryModels = queryModels;
    }

    /// <summary>A reach of nothing, so every field refuses.</summary>
    public static ClientDocumentReach None { get; } = new([], []);

    /// <summary>The exposed trains in <paramref name="trains"/> the schema serves, and every query model.</summary>
    public static ClientDocumentReach Of(
        ISchemaDefinition schema,
        IReadOnlyDictionary<TrainRegistration, string> trains,
        IEnumerable<QueryModelRegistration> queryModels
    ) =>
        new(
            ExposedTrains
                .Resolve(schema, trains)
                .Select(t => (t.Root, t.Namespace ?? "", t.Field))
                .ToHashSet(),
            queryModels
                .Select(r =>
                    (
                        r.Attribute.Namespace is { } ns ? TrainTypeModule.CamelCase(ns) : "",
                        QueryModelTypeModule.FieldName(r)
                    )
                )
                .ToHashSet()
        );

    /// <summary>
    /// A reach of the named trains and query models, as <c>root.namespace.field</c> with an empty namespace for none,
    /// for a test.
    /// </summary>
    internal static ClientDocumentReach Of(
        IEnumerable<(string Root, string? Namespace, string Field)> trains,
        IEnumerable<(string? Namespace, string Field)> queryModels
    ) =>
        new(
            trains.Select(t => (t.Root, t.Namespace ?? "", t.Field)).ToHashSet(),
            queryModels.Select(q => (q.Namespace ?? "", q.Field)).ToHashSet()
        );

    /// <summary>Whether <paramref name="field"/> under <paramref name="ns"/> of <paramref name="root"/> is an exposed train.</summary>
    public bool IsTrain(string root, string? ns, string field) =>
        _trains.Contains((root, ns ?? "", field));

    /// <summary>Whether <paramref name="field"/> under <paramref name="ns"/> of <c>discover</c> is a query model.</summary>
    public bool IsQueryModel(string? ns, string field) => _queryModels.Contains((ns ?? "", field));
}
