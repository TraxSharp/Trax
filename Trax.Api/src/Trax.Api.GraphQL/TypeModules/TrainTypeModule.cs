using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Trax.Api.DTOs;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// A HotChocolate TypeModule that dynamically generates GraphQL queries and mutations
/// from discovered train registrations. Each train marked with [TraxMutation]/[TraxQuery] attributes
/// gets corresponding query/mutation fields wired to the TrainExecutionService.
/// </summary>
public partial class TrainTypeModule(ITrainDiscoveryService discoveryService) : TypeModule
{
    /// <summary>
    /// Discovers all registered trains and generates the GraphQL schema types:
    /// - InputObjectType for each unique input type
    /// - ObjectType for each unique typed output
    /// - Per-train response types for mutations with typed output (e.g. RunCreatePlayerResponse)
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
                // Eagerly create a response wrapper type for Run mutations with typed output
                // (e.g. "RunCreatePlayerResponse" with metadataId + output fields)
                if (HasTypedOutput(reg) && reg.GraphQLOperations.HasFlag(GraphQLOperation.Run))
                    types.Add(BuildRunResponseType(trainName, reg.OutputType));

                mutationFields.Add((reg, trainName));
            }
        }

        // Extend the root mutation type with run*/queue* fields for each mutation train
        if (mutationFields.Count > 0)
        {
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DispatchMutations");
                    foreach (var (reg, name) in mutationFields)
                    {
                        if (reg.GraphQLOperations.HasFlag(GraphQLOperation.Run))
                            AddRunField(d, reg, name);

                        if (reg.GraphQLOperations.HasFlag(GraphQLOperation.Queue))
                            AddQueueField(d, reg, name);
                    }
                })
            );
        }

        // Extend the root query type with fields for each query train
        if (queryFields.Count > 0)
        {
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries");
                    foreach (var (reg, name) in queryFields)
                        AddQueryField(d, reg, name);
                })
            );
        }

        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    /// <summary>
    /// Builds a response wrapper ObjectType for Run mutations that have typed output.
    /// The generated type has two fields: metadataId (Long!) and output (OutputType!).
    /// </summary>
    private static ObjectType BuildRunResponseType(string trainName, Type outputType)
    {
        var responseTypeName = $"Run{trainName}Response";

        return new ObjectType(d =>
        {
            d.Name(responseTypeName);
            d.Field("metadataId")
                .Type<NonNullType<LongType>>()
                .Resolve(ctx => ((RunTrainResult)ctx.Parent<object>()).MetadataId);
            d.Field("output")
                .Type(
                    typeof(NonNullType<>).MakeGenericType(
                        typeof(ObjectType<>).MakeGenericType(outputType)
                    )
                )
                .Resolve(ctx => ((RunTrainResult)ctx.Parent<object>()).Output);
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
