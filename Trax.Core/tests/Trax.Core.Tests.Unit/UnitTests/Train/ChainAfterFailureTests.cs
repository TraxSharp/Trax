using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Once a junction has failed, the junctions after it are skipped. Skipping one has to mean
/// nothing is built for it either: resolving a junction or its dependencies from the container
/// runs their constructors, for a step whose outcome is already decided.
/// </summary>
public class ChainAfterFailureTests : TestSetup
{
    [Test]
    public async Task IChain_AfterAFailedJunction_DoesNotResolveTheJunctionFromTheContainer()
    {
        var built = new Built();
        var services = new ServiceCollection()
            .AddSingleton(built)
            .AddTransient<IAfterJunction, AfterJunction>()
            .BuildServiceProvider();

        var result = await new IChainAfterFailureTrain(services).RunEither("x");

        result.IsLeft.Should().BeTrue();
        built
            .Junctions.Should()
            .Be(0, "the chain had already failed, so the IChain step is skipped");
    }

    [Test]
    public async Task ShortCircuit_AfterAFailedJunction_DoesNotResolveTheJunctionsDependencies()
    {
        var built = new Built();
        var services = new ServiceCollection()
            .AddSingleton(built)
            .AddTransient<Dependency>()
            .BuildServiceProvider();

        var result = await new ShortCircuitAfterFailureTrain(services).RunEither("x");

        result.IsLeft.Should().BeTrue();
        built
            .Dependencies.Should()
            .Be(0, "the chain had already failed, so the ShortCircuit step is skipped");
    }

    [Test]
    public async Task ChainOfAnInstance_AfterAFailedJunction_DoesNotResolveItsInputFromTheContainer()
    {
        var built = new Built();
        var services = new ServiceCollection()
            .AddSingleton(built)
            .AddTransient<IInputService, InputService>()
            .BuildServiceProvider();

        var result = await new InstanceAfterFailureTrain(services).RunEither("x");

        result.IsLeft.Should().BeTrue();
        built
            .Inputs.Should()
            .Be(0, "the chain had already failed, so the instance step is skipped with its input");
    }

    [Test]
    public async Task TypedChainOfAnInstance_AfterAFailedJunction_DoesNotResolveItsInputFromTheContainer()
    {
        var built = new Built();
        var services = new ServiceCollection()
            .AddSingleton(built)
            .AddTransient<IInputService, InputService>()
            .BuildServiceProvider();

        var result = await new TypedInstanceAfterFailureTrain(services).RunEither("x");

        result.IsLeft.Should().BeTrue();
        built
            .Inputs.Should()
            .Be(
                0,
                "the chain had already failed, so the typed instance step is skipped with its input"
            );
    }

    public sealed class Built
    {
        public int Junctions;
        public int Dependencies;
        public int Inputs;
    }

    public sealed class Dependency
    {
        public Dependency(Built built) => built.Dependencies++;
    }

    public interface IAfterJunction : IJunction<string, int>;

    public sealed class AfterJunction : Junction<string, int>, IAfterJunction
    {
        public AfterJunction(Built built) => built.Junctions++;

        public override Task<int> Run(string input) => Task.FromResult(1);
    }

    public sealed class Fails : Junction<string, LanguageExt.Unit>
    {
        public override Task<LanguageExt.Unit> Run(string input) =>
            throw new InvalidOperationException("first junction failed");
    }

    public sealed class ShortCircuitsWithDependency(Dependency dependency) : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(dependency is null ? 0 : 1);
    }

    public sealed class Produces : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(2);
    }

    private sealed class IChainAfterFailureTrain(IServiceProvider services) : Train<string, int>
    {
        protected override Monad<string, int> NewMonad() => new(this, services, CancellationToken);

        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<Fails>().IChain<IAfterJunction>().Resolve();
    }

    private sealed class ShortCircuitAfterFailureTrain(IServiceProvider services)
        : Train<string, int>
    {
        protected override Monad<string, int> NewMonad() => new(this, services, CancellationToken);

        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<Fails>().ShortCircuit<ShortCircuitsWithDependency>().Chain<Produces>().Resolve();
    }

    public interface IInputService;

    public sealed class InputService : IInputService
    {
        public InputService(Built built) => built.Inputs++;
    }

    public sealed class TakesInput : Junction<IInputService, int>
    {
        public override Task<int> Run(IInputService input) => Task.FromResult(3);
    }

    private sealed class InstanceAfterFailureTrain(IServiceProvider services) : Train<string, int>
    {
        protected override Monad<string, int> NewMonad() => new(this, services, CancellationToken);

        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<Fails>().Chain(new TakesInput()).Resolve();
    }

    private sealed class TypedInstanceAfterFailureTrain(IServiceProvider services)
        : Train<string, int>
    {
        protected override Monad<string, int> NewMonad() => new(this, services, CancellationToken);

        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<Fails>().Chain<TakesInput, IInputService, int>(new TakesInput()).Resolve();
    }
}
