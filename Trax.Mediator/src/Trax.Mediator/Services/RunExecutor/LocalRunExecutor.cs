using System.Collections.Concurrent;
using System.Reflection;
using LanguageExt;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Services.RunExecutor;

/// <summary>
/// Default <see cref="IRunExecutor"/> that executes trains in-process via <see cref="ITrainBus"/>.
/// </summary>
public class LocalRunExecutor(ITrainBus trainBus, IDataContextProviderFactory dataContextFactory)
    : IRunExecutor
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> RunAsyncMethodCache = new();

    public async Task<RunTrainResult> ExecuteAsync(
        string trainName,
        object input,
        Type outputType,
        CancellationToken ct = default
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = trainName,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        using var dataContext = await dataContextFactory.CreateDbContextAsync(ct);
        await dataContext.Track(metadata);
        await dataContext.SaveChanges(ct);

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

        return new RunTrainResult(metadata.Id, metadata.ExternalId, output);
    }
}
