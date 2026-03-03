using System.Text.Json;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainExecution;

public class TrainExecutionService(
    ITrainDiscoveryService discoveryService,
    ITrainBus trainBus,
    IDataContextProviderFactory dataContextFactory
) : ITrainExecutionService
{
    public async Task<QueueTrainResult> QueueAsync(
        string trainName,
        string inputJson,
        int priority = 0,
        CancellationToken ct = default
    )
    {
        var registration = FindTrain(trainName);
        var input = DeserializeInput(inputJson, registration);

        var serializedInput = JsonSerializer.Serialize(
            input,
            registration.InputType,
            TraxJsonSerializationOptions.ManifestProperties
        );

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = registration.ServiceType.FullName!,
                Input = serializedInput,
                InputTypeName = registration.InputType.FullName,
                Priority = priority,
            }
        );

        using var dataContext = await dataContextFactory.CreateDbContextAsync(ct);
        await dataContext.Track(entry);
        await dataContext.SaveChanges(ct);

        return new QueueTrainResult(entry.Id, entry.ExternalId);
    }

    public async Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    )
    {
        var registration = FindTrain(trainName);
        var input = DeserializeInput(inputJson, registration);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = registration.ServiceType.FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        using var dataContext = await dataContextFactory.CreateDbContextAsync(ct);
        await dataContext.Track(metadata);
        await dataContext.SaveChanges(ct);

        await trainBus.RunAsync(input, ct, metadata);

        return new RunTrainResult(metadata.Id);
    }

    private TrainRegistration FindTrain(string trainName)
    {
        var registration = discoveryService
            .DiscoverTrains()
            .FirstOrDefault(t => t.ServiceType.FullName == trainName);

        if (registration is null)
            throw new InvalidOperationException(
                $"No train found with name '{trainName}'. "
                    + "Use ITrainDiscoveryService.DiscoverTrains() to list available trains."
            );

        return registration;
    }

    private static object DeserializeInput(string inputJson, TrainRegistration registration)
    {
        var input = JsonSerializer.Deserialize(
            inputJson,
            registration.InputType,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        if (input is null)
            throw new InvalidOperationException(
                $"Deserialization returned null. Ensure the input matches {registration.InputTypeName}."
            );

        return input;
    }
}
