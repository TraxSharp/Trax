using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Mediator.Configuration;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainExecution;

public class TrainExecutionService(
    ITrainDiscoveryService discoveryService,
    IRunExecutor runExecutor,
    IConcurrencyLimiter concurrencyLimiter,
    IDataContextProviderFactory dataContextFactory,
    MediatorConfiguration mediatorConfiguration,
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
        EnforceInputSizeCap(inputJson, registration);
        var input = DeserializeInput(inputJson, registration);

        registration.ServiceType.FullName.AssertLoaded();

        var serializedInput = JsonSerializer.Serialize(
            input,
            registration.InputType,
            TraxJsonSerializationOptions.ManifestProperties
        );

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = registration.ServiceType.FullName,
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
        EnforceInputSizeCap(inputJson, registration);
        var input = DeserializeInput(inputJson, registration);

        registration.ServiceType.FullName.AssertLoaded();

        using var permit = await concurrencyLimiter.AcquireAsync(
            registration.ServiceType.FullName,
            ct
        );

        return await runExecutor.ExecuteAsync(
            registration.ServiceType.FullName,
            input,
            registration.OutputType,
            ct
        );
    }

    private async Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct)
    {
        var authService = serviceProvider.GetService<ITrainAuthorizationService>();

        if (authService is not null)
        {
            await authService.AuthorizeAsync(registration, ct);
            return;
        }

        // Fail closed: if the train carries auth requirements but no enforcer is
        // registered, refuse to execute. Hosts that genuinely run no API submissions
        // (e.g. scheduler-only processes) can opt out via
        // TraxMediatorBuilder.AllowMissingAuthorizationService().
        if (
            registration.HasAuthorizeAttribute
            && !mediatorConfiguration.AllowMissingAuthorizationService
        )
        {
            throw new InvalidOperationException(
                $"Train '{registration.ServiceTypeName}' declares [TraxAuthorize] but no "
                    + "ITrainAuthorizationService is registered. Call AddTraxApi() (or register "
                    + "a custom ITrainAuthorizationService) before building the host. If this "
                    + "process intentionally runs no authorized submissions, opt out with "
                    + "AddMediator(m => m.AllowMissingAuthorizationService())."
            );
        }
    }

    private TrainRegistration FindTrain(string trainName)
    {
        var trains = discoveryService.DiscoverTrains();

        var byFullName = trains.FirstOrDefault(t => t.ServiceType.FullName == trainName);
        if (byFullName is not null)
            return byFullName;

        var byFriendlyName = trains.Where(t => t.ServiceTypeName == trainName).ToList();
        if (byFriendlyName.Count == 1)
            return byFriendlyName[0];
        if (byFriendlyName.Count > 1)
            throw new AmbiguousTrainNameException(
                trainName,
                byFriendlyName.Select(t => t.ServiceType.FullName ?? t.ServiceTypeName).ToList()
            );

        throw new TrainNotFoundException(trainName);
    }

    private void EnforceInputSizeCap(string inputJson, TrainRegistration registration)
    {
        // Enforced post-authorization so unauthenticated callers can't map the cap,
        // and pre-deserialization so oversized JSON never reaches the deserializer.
        // Byte length (UTF-8) is the bounded resource — char length would miscount
        // surrogate pairs and multi-byte sequences.
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(inputJson);
        if (byteCount > mediatorConfiguration.MaxInputJsonBytes)
            throw new TrainInputValidationException(
                registration.ServiceTypeName,
                byteCount,
                mediatorConfiguration.MaxInputJsonBytes
            );
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
