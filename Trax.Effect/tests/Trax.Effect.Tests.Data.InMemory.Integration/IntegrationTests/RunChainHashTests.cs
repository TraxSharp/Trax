using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// A run records the hash of the chain it ran when the host has already read that chain (the
/// startup chain check does), so a run graph drawn later on a changed chain can say so. A run
/// never reads its chain for this: a train whose chain nothing read records none.
/// </summary>
public class RunChainHashTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<IKnownChainTrain, KnownChainTrain>()
            .AddScopedTraxRoute<IUnreadChainTrain, UnreadChainTrain>()
            .BuildServiceProvider();

    [Test]
    public async Task A_run_of_a_train_whose_chain_the_host_read_records_its_hash()
    {
        var chain = new KnownChainTrain().DeclaredChain();
        DeclaredChains.Remember(typeof(KnownChainTrain), chain, typeof(Unit), typeof(Unit));
        var expected = ChainGraph
            .From(chain, typeof(KnownChainTrain), typeof(Unit), typeof(Unit))
            .Hash;

        await Scope.ServiceProvider.GetRequiredService<IKnownChainTrain>().Run(Unit.Default);

        (await ChainHashOf(typeof(IKnownChainTrain).FullName!))
            .Should()
            .Be(expected, "the run records the chain the host read for its train");
    }

    [Test]
    public async Task A_run_of_a_train_whose_chain_nothing_read_records_none_and_reads_none()
    {
        await Scope.ServiceProvider.GetRequiredService<IUnreadChainTrain>().Run(Unit.Default);

        (await ChainHashOf(typeof(IUnreadChainTrain).FullName!)).Should().BeNull();
        DeclaredChains
            .Known(typeof(UnreadChainTrain))
            .Should()
            .BeNull("recording the hash never reads a chain nothing else read");
    }

    private async Task<string?> ChainHashOf(string trainName)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();
        var row = await context.Metadatas.AsNoTracking().SingleAsync(m => m.Name == trainName);
        return row.ChainHash;
    }

    public class Pass : Junction<Unit, Unit>
    {
        public override Task<Unit> Run(Unit input) => Task.FromResult(input);
    }

    public interface IKnownChainTrain : IServiceTrain<Unit, Unit>;

    /// <summary>A train only this test runs, so no other test has its chain remembered.</summary>
    public class KnownChainTrain : ServiceTrain<Unit, Unit>, IKnownChainTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Chain<Pass>().Resolve();
    }

    public interface IUnreadChainTrain : IServiceTrain<Unit, Unit>;

    /// <summary>A train only this test runs, and whose chain nothing reads.</summary>
    public class UnreadChainTrain : ServiceTrain<Unit, Unit>, IUnreadChainTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<Pass>().Chain<Pass>().Resolve();
    }
}
