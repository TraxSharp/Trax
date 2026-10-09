using Trax.Core.Monad;
using Trax.Mediator.Services.ChainVerification;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// The graphs a test hands the run page, by train name; every other name has none, as an
/// unregistered train has none in a host. A train given its declared chain too can be resumed.
/// </summary>
public sealed class FixedChainGraphs : ITrainChainGraphs
{
    private readonly Dictionary<string, ChainGraph> _graphs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeclaredTrainChain> _declared = new(StringComparer.Ordinal);

    /// <summary>Gives <paramref name="train"/> the graph <paramref name="graph"/>.</summary>
    public FixedChainGraphs With(string train, ChainGraph graph)
    {
        _graphs[train] = graph;
        return this;
    }

    /// <summary>
    /// Gives <paramref name="train"/> the declared chain of <typeparamref name="TTrain"/>, and the
    /// graph drawn from it, as a host that registers the train has both.
    /// </summary>
    public FixedChainGraphs WithDeclared<TTrain, TIn, TOut>(string train)
        where TTrain : Trax.Core.Train.Train<TIn, TOut>, new()
    {
        var chain = new TTrain().DeclaredChain();
        _declared[train] = new DeclaredTrainChain(typeof(TTrain), chain, typeof(TIn), typeof(TOut));
        _graphs[train] = ChainGraph.From(chain, typeof(TTrain), typeof(TIn), typeof(TOut));
        return this;
    }

    public ChainGraph? Find(string train) => _graphs.GetValueOrDefault(train);

    public DeclaredTrainChain? FindDeclared(string train) => _declared.GetValueOrDefault(train);
}
