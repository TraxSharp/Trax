using Trax.Core.Monad;
using Trax.Mediator.Services.ChainVerification;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// The graphs a test hands the run page, by train name; every other name has none, as an
/// unregistered train has none in a host.
/// </summary>
public sealed class FixedChainGraphs : ITrainChainGraphs
{
    private readonly Dictionary<string, ChainGraph> _graphs = new(StringComparer.Ordinal);

    /// <summary>Gives <paramref name="train"/> the graph <paramref name="graph"/>.</summary>
    public FixedChainGraphs With(string train, ChainGraph graph)
    {
        _graphs[train] = graph;
        return this;
    }

    public ChainGraph? Find(string train) => _graphs.GetValueOrDefault(train);
}
