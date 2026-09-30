using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainBus;

/// <summary>
/// Running a train by name. The input-keyed <c>RunAsync</c> reaches one train per input type;
/// these reach the train a caller named, which is the one a by-name caller looked up and
/// authorized.
/// </summary>
internal partial class TrainBus
{
    /// <inheritdoc/>
    public async Task<TOut> RunByNameAsync<TOut>(
        string trainName,
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    )
    {
        var registration = FindRegistration(trainName, trainInput);

        await using var scope = scopeFactory.CreateAsyncScope();
        var train = ResolveTrain(scope.ServiceProvider, registration);

        return await (Task<TOut>)InvokeRun(train, trainInput, cancellationToken, metadata);
    }

    /// <inheritdoc/>
    public async Task RunByNameAsync(
        string trainName,
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    )
    {
        var registration = FindRegistration(trainName, trainInput);

        await using var scope = scopeFactory.CreateAsyncScope();
        var train = ResolveTrain(scope.ServiceProvider, registration);

        await InvokeRun(train, trainInput, cancellationToken, metadata);
    }

    /// <summary>
    /// Runs the train named <paramref name="trainName"/> as a <c>Pending</c> record that
    /// <paramref name="createPendingMetadata"/> writes only once the train has been resolved, so
    /// a train that cannot be built (no registration, a dependency missing from DI) leaves no
    /// record behind. Used by <see cref="RunExecutor.LocalRunExecutor"/>.
    /// </summary>
    /// <param name="trainName">The service type's full name of a discovered train.</param>
    /// <param name="trainInput">The input, of that train's input type.</param>
    /// <param name="createPendingMetadata">
    /// Writes and returns the <c>Pending</c> record the train runs as. Called after resolution
    /// and before the train runs.
    /// </param>
    /// <param name="cancellationToken">Passed to <paramref name="createPendingMetadata"/> and the train.</param>
    internal async Task<TOut> RunByNameAsPendingAsync<TOut>(
        string trainName,
        object trainInput,
        Func<CancellationToken, Task<Metadata>> createPendingMetadata,
        CancellationToken cancellationToken
    )
    {
        var registration = FindRegistration(trainName, trainInput);

        await using var scope = scopeFactory.CreateAsyncScope();
        var train = ResolveTrain(scope.ServiceProvider, registration);
        var metadata = await createPendingMetadata(cancellationToken);

        return await (Task<TOut>)InvokeRun(train, trainInput, cancellationToken, metadata);
    }

    /// <summary>
    /// The discovered train whose service type is named <paramref name="trainName"/>, checked
    /// against the input before anything is resolved.
    /// </summary>
    private TrainRegistration FindRegistration(string trainName, object trainInput)
    {
        if (trainInput == null)
            throw new TrainException("trainInput is null as input to TrainBus.RunByNameAsync(...)");

        var registration =
            serviceProvider
                .GetRequiredService<ITrainDiscoveryService>()
                .DiscoverTrains()
                .FirstOrDefault(t => t.ServiceType.FullName == trainName)
            ?? throw new TrainException($"No train is registered under the name ({trainName})");

        if (!registration.InputType.IsInstanceOfType(trainInput))
            throw new TrainException(
                $"Train ({trainName}) takes input of type ({registration.InputType.Name}), "
                    + $"not ({trainInput.GetType().Name})"
            );

        return registration;
    }

    /// <summary>
    /// Resolves the registration's own service type, so the train that runs is the one it
    /// describes, whichever train the registry keeps for its input type.
    /// </summary>
    private static object ResolveTrain(IServiceProvider provider, TrainRegistration registration)
    {
        var train = provider.GetRequiredService(registration.ServiceType);
        provider.InjectProperties(train);

        return train;
    }

    /// <summary>
    /// Calls <c>Run(input, metadata, ct)</c> when <paramref name="metadata"/> is given, and
    /// <c>Run(input, ct)</c> otherwise, through the same method caches as <c>RunAsync</c>.
    /// </summary>
    private static Task InvokeRun(
        object train,
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata
    )
    {
        var trainType = train.GetType();

        if (metadata != null)
        {
            if (metadata.TrainState != TrainState.Pending)
                throw new TrainException(
                    $"TrainBus will not run a passed Metadata with state ({metadata.TrainState}), Must be Pending"
                );

            var withMetadata = RunWithMetadataCtMethodCache.GetOrAdd(
                trainType,
                FindRunWithMetadataAndCancellation
            );

            return (Task?)withMetadata.Invoke(train, [trainInput, metadata, cancellationToken])
                ?? InvocationNull<Task>("Run(input, metadata, ct)", trainType.Name);
        }

        var run = RunWithCtMethodCache.GetOrAdd(trainType, FindRunWithCancellation);

        return (Task?)run.Invoke(train, [trainInput, cancellationToken])
            ?? InvocationNull<Task>("Run(input, ct)", trainType.Name);
    }

    private static MethodInfo FindRunWithMetadataAndCancellation(Type type) =>
        type.GetMethods()
            .Where(x => x.Name == "Run")
            .Where(x => x.GetParameters().Length == 3)
            .FirstOrDefault(x =>
                x.GetParameters()[1].ParameterType == typeof(Metadata)
                && x.GetParameters()[2].ParameterType == typeof(CancellationToken)
            )
        ?? MissingMethod<MethodInfo>("Run(input, metadata, ct)", type.Name);

    private static MethodInfo FindRunWithCancellation(Type type) =>
        type.GetMethods()
            .Where(x => x.Name == "Run")
            .Where(x => x.GetParameters().Length == 2)
            .FirstOrDefault(x =>
                x.GetParameters()[1].ParameterType == typeof(CancellationToken)
                && x.Module.Name.Contains("Effect")
            )
        ?? MissingMethod<MethodInfo>("Run(input, ct)", type.Name);
}
