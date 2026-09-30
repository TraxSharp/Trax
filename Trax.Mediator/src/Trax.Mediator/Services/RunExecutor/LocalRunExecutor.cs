using System.Collections.Concurrent;
using System.Reflection;
using LanguageExt;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;
using DefaultTrainBus = Trax.Mediator.Services.TrainBus.TrainBus;

namespace Trax.Mediator.Services.RunExecutor;

/// <summary>
/// Default <see cref="IRunExecutor"/> that executes trains in-process via <see cref="ITrainBus"/>.
/// </summary>
public class LocalRunExecutor(ITrainBus trainBus, IDataContextProviderFactory dataContextFactory)
    : IRunExecutor
{
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
    /// and the caller looked up and authorized this one. A train that cannot be built (no
    /// registration, or a constructor dependency the container cannot supply) throws before any
    /// record is written, so it leaves no <c>Pending</c> row that nothing will ever move on. This
    /// holds for the default bus; with a bus the host registered in its place, the record is
    /// written before the run. An exception thrown by the train propagates.
    /// </remarks>
    public async Task<RunTrainResult> ExecuteAsync(
        string trainName,
        object input,
        Type outputType,
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

        var output = await (Task<object?>)
            runBoxed.Invoke(
                this,
                [trainName, input, (Func<CancellationToken, Task<Metadata>>)CreatePendingAsync, ct]
            )!;

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

        var metadata = await createPending(ct);
        return await trainBus.RunByNameAsync<TOut>(trainName, input, ct, metadata);
    }
}
