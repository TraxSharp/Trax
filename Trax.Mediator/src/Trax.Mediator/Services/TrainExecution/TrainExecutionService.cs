using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
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
    /// <summary>
    /// Per-train-type cache of the concrete train's overridden <c>OnQueue</c> method, or null when
    /// the train does not override it. Trains that do not override it (the common case) skip
    /// resolution entirely, so the enqueue path stays as light as it was before the hook existed.
    /// The reflection runs once per type.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo?> OnQueueOverrideCache = new();

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

        // Queue-time hook: fire OnQueue before the work queue row is inserted, so a consumer
        // can perform a side-effect (e.g. an optimistic shadow write) the moment the mutation
        // is accepted. The entry's ExternalId is the correlation key — the eventual run executes
        // under the same ExternalId. Exceptions propagate and abort the enqueue.
        await InvokeQueueHookAsync(registration, input, entry.ExternalId, ct);

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

    /// <summary>
    /// Invokes the train's <c>OnQueue</c> hook if the concrete train overrides it. Resolves the
    /// train through its service interface (so <c>[Inject]</c> properties and <c>CanonicalName</c>
    /// are populated exactly as a normal run would), then calls the hook with a non-persisted
    /// metadata carrying the input, canonical name, and the work queue entry's ExternalId.
    /// Exceptions are intentionally not caught: a failed <c>OnQueue</c> aborts the enqueue.
    /// </summary>
    /// <remarks>
    /// The hook is invoked via reflection rather than a marker interface on purpose: a non-generic
    /// interface on <c>ServiceTrain&lt;,&gt;</c> would collide with the canonical-interface
    /// selection in train discovery (it picks the first non-generic interface), so every train
    /// could register under the marker instead of its own interface.
    /// </remarks>
    private async Task InvokeQueueHookAsync(
        TrainRegistration registration,
        object input,
        string externalId,
        CancellationToken ct
    )
    {
        var onQueue = ResolveOnQueueOverride(registration.ImplementationType);
        if (onQueue is null)
            return;

        var train = serviceProvider.GetRequiredService(registration.ServiceType);

        var hookMetadata = Metadata.Create(
            new CreateMetadata
            {
                Name = registration.ServiceType.FullName!,
                ExternalId = externalId,
                Input = input,
            }
        );

        try
        {
            await (Task)onQueue.Invoke(train, [hookMetadata, ct])!;
        }
        catch (TargetInvocationException ex)
        {
            // MethodInfo.Invoke wraps a synchronous throw from the hook. Unwrap so callers see
            // the hook's real exception (with its original stack trace), not the reflection wrapper.
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
        }
    }

    /// <summary>
    /// Returns the train's overridden <c>OnQueue</c> method, or null when the train does not
    /// override the no-op <c>ServiceTrain&lt;,&gt;.OnQueue</c>. Cached per type; the reflection
    /// runs once.
    /// </summary>
    private static MethodInfo? ResolveOnQueueOverride(Type implementationType) =>
        OnQueueOverrideCache.GetOrAdd(
            implementationType,
            static type =>
            {
                var method = type.GetMethod(
                    "OnQueue",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                var declaringType = method?.DeclaringType;
                if (declaringType is { IsGenericType: true })
                    declaringType = declaringType.GetGenericTypeDefinition();

                return declaringType != typeof(ServiceTrain<,>) ? method : null;
            }
        );

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
