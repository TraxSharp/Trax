using Trax.Api.GraphQL.Configuration;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Who puts a field on a type, or names a type, that Trax builds.
/// </summary>
/// <param name="Key">Identifies the owner, so one owner contributing a name twice is not a clash.</param>
/// <param name="Description">How the refusal names it.</param>
internal sealed record SchemaNameOwner(string Key, string Description);

/// <summary>
/// The fields Trax places on its root and namespace types, and the types it generates, with the
/// surface that owns each. Built from the configuration before any schema exists, so two surfaces
/// claiming one name refuse the host, naming both, instead of HotChocolate merging two fields of
/// one name into one whose resolver is whichever came last.
/// See <c>docs/adr/0001-a-misconfigured-host-fails-at-startup.md</c>.
/// </summary>
/// <remarks>
/// Fields are placed by the same helpers the type modules use to build them
/// (<see cref="QueryModelTypeModule.FieldName"/>, <see cref="TrainTypeModule.NamespaceTypeName"/>,
/// <see cref="TrainTypeModule.ResponseTypeName"/>), so the census and the schema cannot disagree
/// about where a field goes. A field a type extension adds is checked against this census while
/// the schema builds, by <see cref="Configuration.SchemaFieldCollisionInterceptor"/>.
/// </remarks>
internal sealed class SchemaNameCensus
{
    private const string Discover = "DiscoverQueries";
    private const string Dispatch = "DispatchMutations";

    private readonly Dictionary<(string Type, string Field), List<SchemaNameOwner>> _fields = [];
    private readonly Dictionary<string, List<SchemaNameOwner>> _types = new(StringComparer.Ordinal);

    /// <summary>The owner Trax recorded for a field, or <c>null</c> when Trax places none there.</summary>
    public SchemaNameOwner? FieldOwner(string typeName, string fieldName) =>
        _fields.TryGetValue((typeName, fieldName), out var owners) ? owners[0] : null;

    /// <summary>Every type name the census places fields on.</summary>
    public IReadOnlySet<string> FieldParents { get; private set; } = new HashSet<string>();

    /// <summary>
    /// Records every field and generated type, and throws naming each pair of owners that claim
    /// the same one.
    /// </summary>
    public static SchemaNameCensus Take(
        GraphQLConfiguration configuration,
        IReadOnlyList<TrainRegistration> trains,
        IReadOnlyDictionary<TrainRegistration, string> trainNames
    )
    {
        var census = new SchemaNameCensus();

        // The entry fields Trax hangs off the roots.
        if (configuration.ModelRegistrations.Count > 0 || trains.Any(t => t.IsQuery))
            census.Field("RootQuery", "discover", Trax("discover"));
        if (trains.Any(t => t.IsMutation))
            census.Field("RootMutation", "dispatch", Trax("dispatch"));
        if (configuration.OperationQueriesExposed)
            census.Field("RootQuery", "operations", Trax("operations"));
        if (configuration.OperationMutationsExposed)
            census.Field("RootMutation", "operations", Trax("operations"));

        foreach (var reg in configuration.ModelRegistrations)
        {
            var parent = census.Parent(Discover, reg.Attribute.Namespace);
            census.Field(
                parent,
                QueryModelTypeModule.FieldName(reg),
                new SchemaNameOwner(
                    $"model:{reg.EntityType.FullName}:{reg.DbContextType.FullName}",
                    $"query model '{reg.EntityType.FullName}' from '{reg.DbContextType.FullName}'"
                )
            );
            census.Type(TrainTypeModule.GraphQLTypeName(reg.EntityType), ClrType(reg.EntityType));
        }

        var outputNames = TrainTypeModule.OutputTypeGraphQLNames(trains);
        foreach (var train in trains.Where(t => t.IsQuery || t.IsMutation))
        {
            var trainName = trainNames[train];
            var owner = new SchemaNameOwner(
                $"train:{train.ServiceType.FullName}",
                $"train '{train.ServiceType.FullName}'"
            );

            census.Field(
                census.Parent(train.IsQuery ? Discover : Dispatch, train.GraphQLNamespace),
                TrainTypeModule.CamelCase(trainName),
                owner
            );

            if (train.IsMutation)
                census.Type(TrainTypeModule.ResponseTypeName(trainName, outputNames), owner);

            if (TrainTypeModule.ContributesOutputType(train))
                census.Type(
                    TrainTypeModule.GraphQLTypeName(train.OutputType),
                    ClrType(train.OutputType)
                );
        }

        census.FieldParents = census._fields.Keys.Select(k => k.Type).ToHashSet();
        census.ThrowOnClashes();
        return census;
    }

    /// <summary>
    /// The type a field goes on: the root namespace type, or the type of the declared namespace,
    /// whose own field on the root namespace type is recorded here as the namespace's.
    /// </summary>
    private string Parent(string rootNamespaceType, string? ns)
    {
        if (ns is null)
            return rootNamespaceType;

        var field = TrainTypeModule.CamelCase(ns);
        var typeName = TrainTypeModule.NamespaceTypeName(field, rootNamespaceType);
        var owner = new SchemaNameOwner(
            $"namespace:{rootNamespaceType}:{field}",
            $"the namespace '{field}'"
        );
        Field(rootNamespaceType, field, owner);
        Type(typeName, owner);
        return typeName;
    }

    private void Field(string type, string field, SchemaNameOwner owner) =>
        Add(_fields, (type, field), owner);

    private void Type(string name, SchemaNameOwner owner) => Add(_types, name, owner);

    private static void Add<TKey>(
        Dictionary<TKey, List<SchemaNameOwner>> map,
        TKey key,
        SchemaNameOwner owner
    )
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var owners))
            map[key] = owners = [];
        if (!owners.Any(o => o.Key == owner.Key))
            owners.Add(owner);
    }

    private void ThrowOnClashes()
    {
        var clashes = _fields
            .Where(f => f.Value.Count > 1)
            .Select(f =>
                $"the field '{f.Key.Field}' on '{f.Key.Type}' is claimed by {Owners(f.Value)}"
            )
            .Concat(
                _types
                    .Where(t => t.Value.Count > 1)
                    .Select(t => $"the type '{t.Key}' is generated for {Owners(t.Value)}")
            )
            .ToList();

        if (clashes.Count == 0)
            return;

        throw new InvalidOperationException(
            "Two GraphQL surfaces claim the same name, and HotChocolate would merge them into one, "
                + "keeping whichever it built last: "
                + string.Join("; ", clashes)
                + ". Give one of them its own name: Name on [TraxQueryModel], [TraxQuery] or "
                + "[TraxMutation], a different Namespace, or [GraphQLName] on a type. See "
                + "docs/adr/0001-a-misconfigured-host-fails-at-startup.md."
        );
    }

    private static string Owners(List<SchemaNameOwner> owners) =>
        string.Join(" and ", owners.Select(o => o.Description));

    private static SchemaNameOwner Trax(string field) =>
        new($"trax:{field}", $"Trax's own '{field}' field");

    private static SchemaNameOwner ClrType(Type type) =>
        new($"type:{type.FullName}", $"the type '{type.FullName}'");
}
