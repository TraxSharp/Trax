using System.Text.Json;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Trax.Api.DTOs;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.GraphQL.TypeModules;

public class TrainTypeModule(ITrainDiscoveryService discoveryService) : TypeModule
{
    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken
    )
    {
        var registrations = discoveryService.DiscoverTrains();
        var types = new List<ITypeSystemMember>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedInputTypes = new HashSet<Type>();
        var trainFields = new List<(TrainRegistration Registration, string TrainName)>();

        foreach (var reg in registrations)
        {
            var trainName = DeriveTrainName(reg.ServiceTypeName);
            if (!usedNames.Add(trainName))
            {
                trainName = DeriveTrainName(reg.ServiceType.FullName ?? reg.ServiceTypeName);
                usedNames.Add(trainName);
            }

            if (usedInputTypes.Add(reg.InputType))
            {
                var inputObjectType = (ITypeSystemMember)
                    Activator.CreateInstance(
                        typeof(InputObjectType<>).MakeGenericType(reg.InputType)
                    )!;
                types.Add(inputObjectType);
            }

            trainFields.Add((reg, trainName));
        }

        if (trainFields.Count > 0)
        {
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name(OperationTypeNames.Mutation);
                    foreach (var (reg, name) in trainFields)
                    {
                        AddRunField(d, reg, name);
                        AddQueueField(d, reg, name);
                    }
                })
            );
        }

        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    private static void AddRunField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var inputType = registration.InputType;
        var serviceTypeName = registration.ServiceTypeName;

        descriptor
            .Field($"run{trainName}")
            .Argument(
                "input",
                a =>
                    a.Type(
                        typeof(NonNullType<>).MakeGenericType(
                            typeof(InputObjectType<>).MakeGenericType(inputType)
                        )
                    )
            )
            .Type<NonNullType<ObjectType<RunTrainResponse>>>()
            .Resolve(async ctx =>
            {
                var input = ctx.ArgumentValue<object>("input");
                var inputJson = JsonSerializer.Serialize(
                    input,
                    inputType,
                    TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                );

                var executionService = ctx.Service<ITrainExecutionService>();
                var ct = ctx.RequestAborted;
                var result = await executionService.RunAsync(serviceTypeName, inputJson, ct);
                return new RunTrainResponse(result.MetadataId);
            });
    }

    private static void AddQueueField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var inputType = registration.InputType;
        var serviceTypeName = registration.ServiceTypeName;

        descriptor
            .Field($"queue{trainName}")
            .Argument(
                "input",
                a =>
                    a.Type(
                        typeof(NonNullType<>).MakeGenericType(
                            typeof(InputObjectType<>).MakeGenericType(inputType)
                        )
                    )
            )
            .Argument("priority", a => a.Type<IntType>())
            .Type<NonNullType<ObjectType<QueueTrainResponse>>>()
            .Resolve(async ctx =>
            {
                var input = ctx.ArgumentValue<object>("input");
                var inputJson = JsonSerializer.Serialize(
                    input,
                    inputType,
                    TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                );

                var priority = ctx.ArgumentValue<int?>("priority") ?? 0;
                var executionService = ctx.Service<ITrainExecutionService>();
                var ct = ctx.RequestAborted;
                var result = await executionService.QueueAsync(
                    serviceTypeName,
                    inputJson,
                    priority,
                    ct
                );
                return new QueueTrainResponse(result.WorkQueueId, result.ExternalId);
            });
    }

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
