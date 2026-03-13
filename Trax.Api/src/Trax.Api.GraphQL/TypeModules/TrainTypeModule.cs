using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
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
public partial class TrainTypeModule(ITrainDiscoveryService discoveryService) : TypeModule
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

            // Derive a unique GraphQL name — fall back to fully-qualified name on collision
            var trainName = reg.GraphQLName ?? DeriveTrainName(reg.ServiceTypeName);
            if (!usedNames.Add(trainName))
            {
                trainName = DeriveTrainName(reg.ServiceType.FullName ?? reg.ServiceTypeName);
                usedNames.Add(trainName);
            }

            // Register HotChocolate InputObjectType / ObjectType once per CLR type
            if (usedInputTypes.Add(reg.InputType))
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
                    d.Name("DispatchMutations");
                    foreach (var (reg, name) in mutationFields)
                        AddMutationField(d, reg, name);
                })
            );
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name("RootMutation");
                    d.Field("dispatch")
                        .Type<ObjectType<DispatchMutations>>()
                        .Resolve(_ => new DispatchMutations());
                })
            );
        }

        // Register DiscoverQueries type + extend RootQuery with a "discover" field,
        // but only when there are query trains.
        if (queryFields.Count > 0)
        {
            types.Add(new ObjectType<DiscoverQueries>());
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries");
                    foreach (var (reg, name) in queryFields)
                        AddQueryField(d, reg, name);
                })
            );
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

        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
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
    /// Returns true if the train produces a meaningful output type
    /// (not Unit and not bare object).
    /// </summary>
    private static bool HasTypedOutput(TrainRegistration registration) =>
        registration.OutputType != typeof(LanguageExt.Unit)
        && registration.OutputType != typeof(object);

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
}
