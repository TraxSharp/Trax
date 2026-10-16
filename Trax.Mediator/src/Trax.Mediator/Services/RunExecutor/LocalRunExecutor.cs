using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.RunOutcomes;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;
using DefaultTrainBus = Trax.Mediator.Services.TrainBus.TrainBus;

namespace Trax.Mediator.Services.RunExecutor;

/// <summary>
/// Default <see cref="IRunExecutor"/> that executes trains in-process via <see cref="ITrainBus"/>.
/// </summary>
public class LocalRunExecutor : IRunExecutor
{
    private readonly ITrainBus trainBus;
    private readonly IDataContextProviderFactory dataContextFactory;

    /// <summary>
    /// Where the lifecycle hooks of a run this executor closes are built from. <c>null</c> when the
    /// executor was constructed by hand rather than by <c>AddMediator</c>; such a run's outcome is
    /// then recorded on its row but not published.
    /// </summary>
    private readonly IServiceProvider? services;

    /// <summary>
    /// Creates an executor that runs trains through <paramref name="trainBus"/> and records their
    /// runs through <paramref name="dataContextFactory"/>.
    /// </summary>
    public LocalRunExecutor(ITrainBus trainBus, IDataContextProviderFactory dataContextFactory)
        : this(trainBus, dataContextFactory, services: null) { }

    internal LocalRunExecutor(
        ITrainBus trainBus,
        IDataContextProviderFactory dataContextFactory,
        IServiceProvider? services
    )
    {
        this.trainBus = trainBus;
        this.dataContextFactory = dataContextFactory;
        this.services = services;
    }

    /// <summary>
    /// <see cref="RunBoxedAsync{TOut}"/> closed over each output type. The output is read through
    /// this generic method rather than as <c>((dynamic)task).Result</c>: the runtime binder binds
    /// against types accessible from this assembly, so for an output type that is not public it
    /// found no <c>Result</c> and threw after the train had already completed.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> RunBoxedMethodCache = new();

    private static readonly MethodInfo RunBoxedDefinition = typeof(LocalRunExecutor).GetMethod(
        nameof(RunBoxedAsync),
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    /// <summary>
    /// Resolves the train registered under <paramref name="trainName"/>, then creates and saves a
    /// <c>Pending</c> metadata record for it with a new external id, then runs it as that record, in
    /// a child DI scope. Returns the record's id and external id, and the output, which is null when
    /// <paramref name="outputType"/> is <c>Unit</c>.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name, stored as the metadata name.</param>
    /// <param name="input">The deserialized input, of the named train's input type.</param>
    /// <param name="outputType">The train's output type, used to call the matching generic <c>RunAsync</c>.</param>
    /// <param name="ct">Passed to the save and to the run.</param>
    /// <exception cref="Trax.Core.Exceptions.TrainException">
    /// No train is registered under <paramref name="trainName"/>, or the input is not of its input type.
    /// </exception>
    /// <remarks>
    /// The train is found by name, not by input type: another train may take the same input type,
    /// and the caller looked up and authorized this one. With the default bus, a train that cannot
    /// be built (no registration, or a constructor dependency the container cannot supply) throws
    /// before any record is written. A bus the host registered in its place that does not
    /// implement <c>RunByNameAsync</c> is refused before any record is written too. Whatever
    /// throws after the record is written and before the train takes it over (a bus that cannot
    /// build the train, a cancellation in between) leaves the record <c>Failed</c>, or
    /// <c>Cancelled</c> for a cancellation, rather than <c>Pending</c> with nothing to move it
    /// on. An exception thrown by the train propagates, and the train records its own outcome.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The registered <see cref="ITrainBus"/> is not the default one and does not implement
    /// <c>RunByNameAsync</c>.
    /// </exception>
    public Task<RunTrainResult> ExecuteAsync(
        string trainName,
        object input,
        Type outputType,
        CancellationToken ct = default
    ) => ExecuteAsync(trainName, input, outputType, starterHash: null, ct);

    /// <inheritdoc/>
    public async Task<RunTrainResult> ExecuteAsync(
        string trainName,
        object input,
        Type outputType,
        string? starterHash,
        CancellationToken ct = default
    )
    {
        Metadata? pending = null;

        async Task<Metadata> CreatePendingAsync(CancellationToken token)
        {
            var metadata = Metadata.Create(
                new CreateMetadata
                {
                    Name = trainName,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    StarterHash = starterHash,
                }
            );

            using var dataContext = await dataContextFactory.CreateDbContextAsync(token);
            await dataContext.Track(metadata);
            await dataContext.SaveChanges(token);

            pending = metadata;
            return metadata;
        }

        var runBoxed = RunBoxedMethodCache.GetOrAdd(
            outputType,
            type => RunBoxedDefinition.MakeGenericMethod(type)
        );

        object? output;

        try
        {
            output = await (Task<object?>)
                runBoxed.Invoke(
                    this,
                    [
                        trainName,
                        input,
                        (Func<CancellationToken, Task<Metadata>>)CreatePendingAsync,
                        ct,
                    ]
                )!;
        }
        catch (Exception ex) when (pending is not null)
        {
            await CloseIfStillPendingAsync(pending.Id, ex);
            throw;
        }

        pending.AssertLoaded();

        return new RunTrainResult(
            pending.Id,
            pending.ExternalId,
            outputType == typeof(Unit) ? null : output
        );
    }

    private async Task<object?> RunBoxedAsync<TOut>(
        string trainName,
        object input,
        Func<CancellationToken, Task<Metadata>> createPending,
        CancellationToken ct
    )
    {
        if (trainBus is DefaultTrainBus bus)
            return await bus.RunByNameAsPendingAsync<TOut>(trainName, input, createPending, ct);

        // The interface's own body only throws, so a bus that keeps it would fail every run after
        // its record was written.
        if (!ImplementsRunByName(trainBus.GetType()))
            throw new NotSupportedException(
                $"{trainBus.GetType().Name} does not implement {nameof(ITrainBus.RunByNameAsync)}, "
                    + "which running a train by name needs. Implement it, or forward it to the "
                    + "bus AddMediator registers."
            );

        var metadata = await createPending(ct);
        return await trainBus.RunByNameAsync<TOut>(trainName, input, ct, metadata);
    }

    private static readonly ConcurrentDictionary<Type, bool> ImplementsRunByNameCache = new();

    /// <summary>
    /// Whether <paramref name="busType"/> overrides the generic <c>RunByNameAsync</c> rather than
    /// keeping the interface's default body.
    /// </summary>
    private static bool ImplementsRunByName(Type busType) =>
        ImplementsRunByNameCache.GetOrAdd(
            busType,
            type =>
            {
                var map = type.GetInterfaceMap(typeof(ITrainBus));

                for (var i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    var declared = map.InterfaceMethods[i];

                    if (
                        declared.Name == nameof(ITrainBus.RunByNameAsync)
                        && declared.IsGenericMethodDefinition
                    )
                        return map.TargetMethods[i].DeclaringType != typeof(ITrainBus);
                }

                return false;
            }
        );

    /// <summary>
    /// Closes a record this executor wrote when the run failed before the train took it over, so
    /// it is not left <c>Pending</c> with nothing to move it on. A record the train already took
    /// over is left alone: the train records its own outcome. When this closes it, the run's
    /// <c>Failed</c> or <c>Cancelled</c> event goes to the lifecycle hooks, as a train's own outcome
    /// would, so a subscriber following the run is not left waiting.
    /// </summary>
    /// <remarks>
    /// Best effort: the caller is about to rethrow <paramref name="failure"/>, and a store that
    /// cannot be reached now must not replace that exception with its own.
    /// </remarks>
    private async Task CloseIfStillPendingAsync(long metadataId, Exception failure)
    {
        TrainState closed;
        try
        {
            using var dataContext = await dataContextFactory.CreateDbContextAsync(
                CancellationToken.None
            );

            var metadata = await dataContext.Metadatas.FirstOrDefaultAsync(
                m => m.Id == metadataId,
                CancellationToken.None
            );

            if (metadata is null || metadata.TrainState != TrainState.Pending)
                return;

            if (failure is OperationCanceledException)
                metadata.TrainState = TrainState.Cancelled;
            else
            {
                metadata.AddException(failure);
                metadata.TrainState = TrainState.Failed;
            }

            metadata.EndTime = DateTime.UtcNow;
            await dataContext.SaveChanges(CancellationToken.None);
            closed = metadata.TrainState;
        }
        catch (Exception)
        {
            // Left Pending; the original failure is what the caller needs to see.
            return;
        }

        if (services is null)
            return;

        // After the write has committed, and only for the run this executor moved. The helper
        // reads the row back, so the hooks see what was recorded, and it never throws.
        var logger =
            services.GetService<ILogger<LocalRunExecutor>>()
            ?? NullLogger<LocalRunExecutor>.Instance;
        if (closed == TrainState.Cancelled)
            await OutOfTrainOutcomes.PublishCancelledAsync(services, [metadataId], logger);
        else
            await OutOfTrainOutcomes.PublishFailedAsync(services, [metadataId], logger);
    }
}
