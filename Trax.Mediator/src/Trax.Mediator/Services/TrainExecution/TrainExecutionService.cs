using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainExecution;

public class TrainExecutionService(
    ITrainDiscoveryService discoveryService,
    IRunExecutor runExecutor,
    IDataContextProviderFactory dataContextFactory,
    IServiceProvider serviceProvider
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
        await AuthorizeAsync(registration, ct);
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
        await AuthorizeAsync(registration, ct);
        var input = DeserializeInput(inputJson, registration);

        return await runExecutor.ExecuteAsync(
            registration.ServiceTypeName,
            input,
            registration.OutputType,
            ct
        );
    }

    private async Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct)
    {
        var authService = serviceProvider.GetService<ITrainAuthorizationService>();
        if (authService is not null)
            await authService.AuthorizeAsync(registration, ct);
    }

    private TrainRegistration FindTrain(string trainName)
    {
        var trains = discoveryService.DiscoverTrains();

        // Exact match on fully qualified name first, then fall back to short name
        var registration =
            trains.FirstOrDefault(t => t.ServiceTypeName == trainName)
            ?? trains.FirstOrDefault(t => t.ServiceType.Name == trainName);

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
