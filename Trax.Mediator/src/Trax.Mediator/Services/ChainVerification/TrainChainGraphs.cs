using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Monad;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.ChainVerification;

/// <inheritdoc />
internal sealed class TrainChainGraphs(
    ITrainDiscoveryService discoveryService,
    IServiceScopeFactory scopeFactory,
    ILogger<TrainChainGraphs>? logger = null
) : ITrainChainGraphs
{
    private readonly ConcurrentDictionary<string, Lazy<ChainGraph?>> _graphs = new(
        StringComparer.Ordinal
    );

    public ChainGraph? Find(string train)
    {
        if (string.IsNullOrWhiteSpace(train))
            return null;

        var registration = discoveryService
            .DiscoverTrains()
            .FirstOrDefault(r =>
                string.Equals(r.ServiceType.FullName, train, StringComparison.Ordinal)
            );

        if (registration is null)
            return null;

        return _graphs.GetOrAdd(train, _ => new Lazy<ChainGraph?>(() => Read(registration))).Value;
    }

    private ChainGraph? Read(TrainRegistration registration)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var train = scope.ServiceProvider.GetRequiredService(registration.ServiceType);

            var chain = (ChainRecorder)
                train
                    .GetType()
                    .GetMethod(nameof(Core.Train.Train<,>.DeclaredChain), Type.EmptyTypes)!
                    .Invoke(train, null)!;

            return ChainGraph.From(
                chain,
                train.GetType(),
                registration.InputType,
                registration.OutputType
            );
        }
        catch (Exception e)
        {
            // Kept as null: building the train failed here, and would fail the same way again.
            logger?.LogWarning(
                e,
                "The chain of train {Train} could not be read outside a request, so it has no graph.",
                registration.ServiceType.FullName
            );

            return null;
        }
    }
}
