using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainExecution;

public class TrainExecutionService(
    ITrainDiscoveryService discoveryService,
    ITrainBus trainBus,
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

    private static readonly ConcurrentDictionary<Type, MethodInfo> RunAsyncMethodCache = new();

    public async Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    )
    {
        var registration = FindTrain(trainName);
        await AuthorizeAsync(registration, ct);
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

        var outputType = registration.OutputType;

        // Use the typed RunAsync<TOut> overload to capture the train's output.
        // For Unit trains, output is discarded (null). For typed trains, the
        // actual output object is returned so GraphQL can expose it.
        var genericMethod = RunAsyncMethodCache.GetOrAdd(
            outputType,
            type =>
                typeof(ITrainBus)
                    .GetMethods()
                    .First(m =>
                        m.Name == "RunAsync"
                        && m.IsGenericMethod
                        && m.GetParameters().Length == 3
                        && m.GetParameters()[1].ParameterType == typeof(CancellationToken)
                    )
                    .MakeGenericMethod(type)
        );

        var task = (Task)genericMethod.Invoke(trainBus, [input, ct, metadata])!;
        await task;

        object? output = outputType == typeof(Unit) ? null : ((dynamic)task).Result;

        return new RunTrainResult(metadata.Id, output);
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
