using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// A HotChocolate TypeModule that dynamically generates GraphQL queries and mutations
/// from discovered train registrations. Each train marked with [TraxMutation]/[TraxQuery] attributes
/// gets a corresponding mutation/query field wired to the TrainExecutionService.
/// </summary>
public partial class TrainTypeModule(
    ITrainDiscoveryService discoveryService,
    GraphQLConfiguration? graphQLConfiguration = null
) : TypeModule
{
    /// <summary>
    /// Discovers all registered trains and generates the GraphQL schema types:
    /// - InputObjectType for each unique input type
    /// - ObjectType for each unique typed output
    /// - Per-train response types for mutations (e.g. CreatePlayerResponse)
    /// - ExecutionMode enum type (when any train supports both Run and Queue)
    /// - ObjectTypeExtension on "DispatchMutations" / "DiscoverQueries" to add fields
    /// </summary>
    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken
    )
    {
        var registrations = discoveryService.DiscoverTrains();
        var types = new List<ITypeSystemMember>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedInputTypes = new HashSet<Type>();
        var usedOutputTypes = new HashSet<Type>();
        var mutationFields = new List<(TrainRegistration Registration, string TrainName)>();
        var queryFields = new List<(TrainRegistration Registration, string TrainName)>();
        var needsExecutionModeEnum = false;

        foreach (var reg in registrations)
        {
            if (!reg.IsQuery && !reg.IsMutation)
                continue;

            // Unit input is not allowed on GraphQL-exposed trains — each train must have
            // a dedicated input record for mediator routing and schema generation.
            if (reg.InputType == typeof(LanguageExt.Unit))
            {
                var attrType = reg.IsQuery ? "[TraxQuery]" : "[TraxMutation]";
                throw new InvalidOperationException(
                    $"Train '{reg.ServiceType.FullName}' has Unit input but is annotated with {attrType}. "
                        + "GraphQL-exposed trains must have a dedicated input record. "
                        + $"Create a type like: public record {DeriveTrainName(reg.ServiceTypeName)}Input;"
                );
            }

            // Derive a unique GraphQL name — fall back to fully-qualified name on collision
            var trainName = reg.GraphQLName ?? DeriveTrainName(reg.ServiceTypeName);
            if (!usedNames.Add(trainName))
            {
                trainName = DeriveTrainName(reg.ServiceType.FullName ?? reg.ServiceTypeName);
                usedNames.Add(trainName);
            }

            // Register HotChocolate InputObjectType / ObjectType once per CLR type.
            // Skip Unit — it has no properties, so InputObjectType<Unit> is invalid in HotChocolate.
            if (HasTypedInput(reg) && usedInputTypes.Add(reg.InputType))
            {
                var inputObjectType = (ITypeSystemMember)
                    Activator.CreateInstance(
                        typeof(InputObjectType<>).MakeGenericType(reg.InputType)
                    )!;
                types.Add(inputObjectType);
            }

            if (HasTypedOutput(reg) && usedOutputTypes.Add(reg.OutputType))
            {
                var outputObjectType = (ITypeSystemMember)
                    Activator.CreateInstance(typeof(ObjectType<>).MakeGenericType(reg.OutputType))!;
                types.Add(outputObjectType);
            }

            if (reg.IsQuery)
            {
                queryFields.Add((reg, trainName));
            }
            else
            {
                // Every mutation train gets a response type
                types.Add(BuildResponseType(trainName, reg));

                if (
                    reg.GraphQLOperations.HasFlag(GraphQLOperation.Run)
                    && reg.GraphQLOperations.HasFlag(GraphQLOperation.Queue)
                )
                    needsExecutionModeEnum = true;

                mutationFields.Add((reg, trainName));
            }
        }

        // Register ExecutionMode enum if any train supports both modes
        if (needsExecutionModeEnum)
            types.Add(BuildExecutionModeEnumType());

        // Register DispatchMutations type + extend RootMutation with a "dispatch" field,
        // but only when there are mutation trains — avoids empty object types in the schema.
        if (mutationFields.Count > 0)
        {
            types.Add(new ObjectType<DispatchMutations>());
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name("RootMutation");
                    d.Field("dispatch")
                        .Type<ObjectType<DispatchMutations>>()
                        .Resolve(_ => new DispatchMutations());
                })
            );

            AddGroupedFields(types, mutationFields, "DispatchMutations", AddMutationField);
        }

        // Register DiscoverQueries type + extend RootQuery with a "discover" field.
        // When query model registrations exist, the base type and discover field are
        // already registered in GraphQLServiceExtensions — only add the field extension.
        var discoverBaseRegisteredExternally = graphQLConfiguration?.ModelRegistrations.Count > 0;

        if (queryFields.Count > 0)
        {
            if (!discoverBaseRegisteredExternally)
            {
                types.Add(new ObjectType<DiscoverQueries>());
                types.Add(
                    new ObjectTypeExtension(d =>
                    {
                        d.Name("RootQuery");
                        d.Field("discover")
                            .Type<ObjectType<DiscoverQueries>>()
                            .Resolve(_ => new DiscoverQueries());
                    })
                );
            }

            AddGroupedFields(types, queryFields, "DiscoverQueries", AddQueryField);
        }

        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    /// <summary>
    /// Groups fields by namespace and creates the appropriate type extensions.
    /// Fields with no namespace go directly on the parent type. Fields with a namespace
    /// get an intermediate ObjectType (e.g. "AlertsDiscoverQueries") and a field on the
    /// parent type pointing to it.
    /// </summary>
    private void AddGroupedFields(
        List<ITypeSystemMember> types,
        List<(TrainRegistration Registration, string TrainName)> fields,
        string parentTypeName,
        Action<IObjectTypeDescriptor, TrainRegistration, string> addField
    )
    {
        var byNamespace = fields.GroupBy(f => f.Registration.GraphQLNamespace);

        foreach (var group in byNamespace)
        {
            if (group.Key is null)
            {
                // No namespace — add fields directly to the parent type
                types.Add(
                    new ObjectTypeExtension(d =>
                    {
                        d.Name(parentTypeName);
                        foreach (var (reg, name) in group)
                            addField(d, reg, name);
                    })
                );
            }
            else
            {
                // Namespace — create intermediate type and add fields to it
                var nsTypeName = NamespaceTypeName(group.Key, parentTypeName);
                var nsFieldName = CamelCase(group.Key);

                // Register the base ObjectType for this namespace (only once across modules)
                if (graphQLConfiguration?.RegisteredNamespaceTypes.Add(nsTypeName) ?? true)
                {
                    types.Add(new ObjectType(d => d.Name(nsTypeName)));
                }

                // Add fields to the namespace type
                types.Add(
                    new ObjectTypeExtension(d =>
                    {
                        d.Name(nsTypeName);
                        foreach (var (reg, name) in group)
                            addField(d, reg, name);
                    })
                );

                // Add the namespace field to the parent type (only once across modules)
                var nsFieldKey = $"{parentTypeName}.{nsFieldName}";
                if (graphQLConfiguration?.RegisteredNamespaceTypes.Add(nsFieldKey) ?? true)
                {
                    var capturedNsTypeName = nsTypeName;
                    types.Add(
                        new ObjectTypeExtension(d =>
                        {
                            d.Name(parentTypeName);
                            d.Field(nsFieldName)
                                .Type(new NamedTypeNode(capturedNsTypeName))
                                .Resolve(_ => new object());
                        })
                    );
                }
            }
        }
    }

    /// <summary>
    /// Builds the ExecutionMode enum type with RUN and QUEUE values.
    /// </summary>
    private static EnumType BuildExecutionModeEnumType()
    {
        return new EnumType(d =>
        {
            d.Name("ExecutionMode");
            d.Description("Controls how a train mutation is executed.");
            d.Value("RUN").Description("Execute synchronously and return the result.");
            d.Value("QUEUE").Description("Queue for asynchronous execution via the scheduler.");
        });
    }

    /// <summary>
    /// Builds a response ObjectType for a mutation train. The response always includes
    /// externalId (non-null). Other fields (metadataId, output, workQueueId) are nullable
    /// and populated based on the execution mode.
    /// </summary>
    private static ObjectType BuildResponseType(string trainName, TrainRegistration registration)
    {
        var responseTypeName = $"{trainName}Response";
        var hasTypedOutput = HasTypedOutput(registration);

        return new ObjectType(d =>
        {
            d.Name(responseTypeName);

            d.Field("externalId")
                .Type<NonNullType<StringType>>()
                .Resolve(ctx =>
                    ctx.Parent<object>() switch
                    {
                        RunTrainResult r => r.ExternalId,
                        QueueTrainResult q => q.ExternalId,
                        _ => throw new InvalidOperationException("Unexpected parent type"),
                    }
                );

            d.Field("metadataId")
                .Type<LongType>()
                .Resolve(ctx =>
                    ctx.Parent<object>() is RunTrainResult r ? (long?)r.MetadataId : null
                );

            d.Field("workQueueId")
                .Type<LongType>()
                .Resolve(ctx =>
                    ctx.Parent<object>() is QueueTrainResult q ? (long?)q.WorkQueueId : null
                );

            if (hasTypedOutput)
            {
                d.Field("output")
                    .Type(typeof(ObjectType<>).MakeGenericType(registration.OutputType))
                    .Resolve(ctx => ctx.Parent<object>() is RunTrainResult r ? r.Output : null);
            }
        });
    }

    /// <summary>
    /// Returns true if the train's input type has at least one GraphQL-representable property.
    /// Empty records (no properties) are allowed as input types for mediator routing uniqueness,
    /// but HotChocolate cannot create an InputObjectType for them — so no input argument
    /// is registered on the GraphQL field.
    /// </summary>
    private static bool HasTypedInput(TrainRegistration registration) =>
        HasGraphQLRepresentableProperties(registration.InputType);

    /// <summary>
    /// Returns true if the train produces a meaningful output type that HotChocolate
    /// can represent as an ObjectType with at least one field.
    /// Excludes Unit, bare object, types with no properties, and types whose
    /// properties are all typed as System.Object (which HotChocolate ignores).
    /// </summary>
    private static bool HasTypedOutput(TrainRegistration registration) =>
        registration.OutputType != typeof(LanguageExt.Unit)
        && registration.OutputType != typeof(object)
        && HasGraphQLRepresentableProperties(registration.OutputType);

    /// <summary>
    /// Returns true if the type has at least one public property whose type
    /// is not System.Object. HotChocolate silently skips object-typed properties,
    /// so a type with only object properties ends up with zero fields.
    /// </summary>
    private static bool HasGraphQLRepresentableProperties(Type type) =>
        type.GetProperties().Any(p => p.PropertyType != typeof(object));

    /// <summary>
    /// Derives a PascalCase GraphQL name from a train's type name.
    /// Strips the leading "I" (interface convention) and trailing "Train" suffix.
    /// e.g. "ICreatePlayerTrain" → "CreatePlayer"
    /// </summary>
    private static string DeriveTrainName(string serviceTypeName)
    {
        var name = serviceTypeName;

        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name[1..];

        if (name.EndsWith("Train", StringComparison.Ordinal))
            name = name[..^5];

        return name;
    }

    /// <summary>
    /// Builds the HotChocolate type name for a namespace group.
    /// e.g. ("alerts", "DiscoverQueries") → "AlertsDiscoverQueries"
    /// </summary>
    internal static string NamespaceTypeName(string ns, string parentTypeName) =>
        PascalCase(ns) + parentTypeName;

    /// <summary>
    /// Capitalizes the first character of a string.
    /// </summary>
    internal static string PascalCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>
    /// Lowercases the first character of a string.
    /// </summary>
    internal static string CamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
