using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Where a refusal sits in the chain, and the opt-in check of the constructor arguments of each
/// junction Trax builds.
///
/// <para>A host numbers what it reports by step, and tells a refusal from the faults that follow
/// from it, so both have to come from <c>Verify</c> rather than from matching its text. A junction
/// whose constructor needs something nothing supplies fails every run; with the container in
/// hand, that is as decidable at startup as a missing input.</para>
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class ChainVerificationStepAndConstructorTests : TestSetup
{
    private static IReadOnlyList<ChainFault> Verify<TTrain>(IServiceCollection? services = null)
        where TTrain : Train<string, bool>, new()
    {
        using var provider = (services ?? new ServiceCollection()).BuildServiceProvider();

        return ChainVerification.Verify(
            new TTrain().DeclaredChain(),
            typeof(string),
            typeof(bool),
            provider.GetRequiredService<IServiceProviderIsService>()
        );
    }

    #region Refusals carry their step

    [Test]
    public void DeclaredChain_AStepNamingANonJunction_KeepsLaterStepsAtTheirWrittenPositions()
    {
        var steps = new NotAJunctionFirstTrain().DeclaredChain().Steps;

        steps
            .Select(s => s.Junction)
            .Should()
            .Equal(typeof(NotAJunction), typeof(StringToBool), null);
    }

    [Test]
    public void Verify_AStepNamingANonJunction_IsARefusalAtThatStep()
    {
        var faults = ChainVerification.Verify(
            new NotAJunctionFirstTrain().DeclaredChain(),
            typeof(string),
            typeof(bool)
        );

        var refusal = faults.Should().ContainSingle(f => f.IsRefusal).Which;
        refusal.StepIndex.Should().Be(0);
        refusal.Kind.Should().Be(ChainStepKind.Chain);
        refusal.Junction.Should().Be(typeof(NotAJunction));
        faults.Should().ContainSingle("the refused step carries no types to fault on");
    }

    [Test]
    public void Verify_AJunctionWithTwoConstructors_IsARefusalAtItsStep()
    {
        var refusal = ChainVerification
            .Verify(new TwoConstructorsSecondTrain().DeclaredChain(), typeof(string), typeof(bool))
            .Should()
            .ContainSingle()
            .Which;

        refusal.IsRefusal.Should().BeTrue();
        refusal.StepIndex.Should().Be(1);
        refusal.Junction.Should().Be(typeof(TwoConstructors));
        refusal.Reason.Should().Contain("2 public constructors");
    }

    [Test]
    public void DeclaredChain_ANonJunctionWithTwoConstructors_IsRefusedOnce() =>
        new NotAJunctionWithTwoConstructorsTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle(
                "whether a type that is not a junction could be built is beside the point"
            )
            .Which.Should()
            .Contain("does not implement");

    [Test]
    public void Verify_ANullService_IsARefusalAtItsSeedStep()
    {
        var refusal = ChainVerification
            .Verify(new NullServiceSecondTrain().DeclaredChain(), typeof(string), typeof(bool))
            .Should()
            .ContainSingle(f => f.IsRefusal)
            .Which;

        refusal.StepIndex.Should().Be(1);
        refusal.Kind.Should().Be(ChainStepKind.Seed);
    }

    [Test]
    public void Verify_ARefusalOfTheWholeChain_SitsPastTheLastStepWithNoJunction()
    {
        var chain = new ResolvesAValueTrain().DeclaredChain();

        var refusal = ChainVerification
            .Verify(chain, typeof(string), typeof(bool))
            .Should()
            .ContainSingle(f => f.IsRefusal)
            .Which;

        refusal.StepIndex.Should().Be(chain.Steps.Count);
        refusal.Kind.Should().Be(ChainStepKind.Resolve);
        refusal.Junction.Should().BeNull();
    }

    [Test]
    public void Verify_AFaultTheReplayFound_IsNotARefusal() =>
        ChainVerification
            .Verify(new MissingInputTrain().DeclaredChain(), typeof(string), typeof(bool))
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(f => !f.IsRefusal);

    #endregion

    #region Constructor arguments

    [Test]
    public void Verify_AConstructorArgumentNothingSupplies_IsAFaultNamingTheJunctionAndTheType()
    {
        var fault = Verify<NeedsClockTrain>().Should().ContainSingle().Which;

        fault.IsRefusal.Should().BeFalse();
        fault.StepIndex.Should().Be(0);
        fault.Junction.Should().Be(typeof(ClockJunction));
        fault.Reason.Should().Contain(nameof(IClock)).And.Contain("constructor argument");
    }

    [Test]
    public void Verify_AConstructorArgumentTheContainerRegisters_IsNotAFault() =>
        Verify<NeedsClockTrain>(new ServiceCollection().AddSingleton<IClock, Clock>())
            .Should()
            .BeEmpty();

    [Test]
    public void Verify_AConstructorArgumentAnEarlierJunctionProduces_IsNotAFault() =>
        Verify<ClockProducedFirstTrain>().Should().BeEmpty();

    [Test]
    public void Verify_AConstructorArgumentOnlyALaterJunctionProduces_IsAFault() =>
        Verify<ClockProducedAfterTrain>()
            .Should()
            .ContainSingle()
            .Which.Junction.Should()
            .Be(typeof(ClockJunction));

    [Test]
    public void Verify_AConstructorArgumentPassedToAddServices_IsNotAFault() =>
        Verify<ClockPassedAsServiceTrain>().Should().BeEmpty();

    [Test]
    public void Verify_TheServiceProviderItself_IsAlwaysAvailable() =>
        Verify<NeedsServiceProviderTrain>().Should().BeEmpty();

    [Test]
    public void Verify_ALoggerWithLoggingRegistered_IsNotAFault() =>
        Verify<NeedsLoggerTrain>(new ServiceCollection().AddLogging()).Should().BeEmpty();

    [Test]
    public void Verify_ALoggerWithAFactoryPassedToAddServices_IsNotAFault() =>
        Verify<NeedsLoggerWithFactoryTrain>().Should().BeEmpty();

    [Test]
    public void Verify_ALoggerWithNoLoggingAndNoFactory_IsAFault() =>
        Verify<NeedsLoggerTrain>()
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("ILogger");

    [Test]
    public void Verify_ATupleConstructorArgument_IsNotTakenFromTheContainer()
    {
        var services = new ServiceCollection().AddSingleton<IClock, Clock>();
        services.Add(
            ServiceDescriptor.Singleton(typeof((string, IClock)), _ => ("x", (IClock)new Clock()))
        );

        Verify<NeedsTupleTrain>(services)
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain(
                "tuple",
                "the runtime assembles a tuple from Memory and never asks the container"
            );
    }

    [Test]
    public void Verify_AJunctionPassedAsAnInstance_HasItsConstructorLeftUnchecked() =>
        Verify<ClockInstanceTrain>().Should().BeEmpty();

    [Test]
    public void Verify_WithoutTheContainer_LeavesConstructorsUnchecked() =>
        ChainVerification
            .Verify(new NeedsClockTrain().DeclaredChain(), typeof(string), typeof(bool))
            .Should()
            .BeEmpty();

    #endregion

    public interface IClock;

    public class Clock : IClock;

    public class NotAJunction;

    public class NotAJunctionWithTwoConstructors
    {
        public NotAJunctionWithTwoConstructors() { }

        public NotAJunctionWithTwoConstructors(IClock clock) => _ = clock;
    }

    private class StringToBool : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(input.Length > 0);
    }

    private class StringToInt : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class IntToBool : Junction<int, bool>
    {
        public override Task<bool> Run(int input) => Task.FromResult(input > 0);
    }

    private class TwoConstructors : Junction<string, bool>
    {
        public TwoConstructors() { }

        public TwoConstructors(IClock clock) => _ = clock;

        public override Task<bool> Run(string input) => Task.FromResult(true);
    }

    private class ClockJunction(IClock clock) : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(clock is not null);
    }

    private class ProducesClock : Junction<string, IClock>
    {
        public override Task<IClock> Run(string input) => Task.FromResult<IClock>(new Clock());
    }

    private class ServiceProviderJunction(IServiceProvider services) : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(services is not null);
    }

    private class LoggerJunction(ILogger<LoggerJunction> logger) : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(logger is not null);
    }

    private class TupleJunction((string, IClock) pair) : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(pair.Item2 is not null);
    }

    private class NotAJunctionFirstTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<NotAJunction>().Chain<StringToBool>().Resolve();
    }

    private class NotAJunctionWithTwoConstructorsTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<NotAJunctionWithTwoConstructors>().Chain<StringToBool>().Resolve();
    }

    private class TwoConstructorsSecondTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToInt>().Chain<TwoConstructors>().Resolve();
    }

    private class NullServiceSecondTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToBool>().AddServices<IClock>(null!).Resolve();
    }

    private class ResolvesAValueTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToBool>().Resolve(true);
    }

    private class MissingInputTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<IntToBool>().Resolve();
    }

    private class NeedsClockTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<ClockJunction>().Resolve();
    }

    private class ClockProducedFirstTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<ProducesClock>().Chain<ClockJunction>().Resolve();
    }

    private class ClockProducedAfterTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<ClockJunction>().Chain<ProducesClock>().Resolve();
    }

    private class ClockPassedAsServiceTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices<IClock>(new Clock()).Chain<ClockJunction>().Resolve();
    }

    private class NeedsServiceProviderTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<ServiceProviderJunction>().Resolve();
    }

    private class NeedsLoggerTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<LoggerJunction>().Resolve();
    }

    private class NeedsLoggerWithFactoryTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices<ILoggerFactory>(new LoggerFactory()).Chain<LoggerJunction>().Resolve();
    }

    private class NeedsTupleTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TupleJunction>().Resolve();
    }

    private class ClockInstanceTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain(new ClockJunction(new Clock())).Resolve();
    }
}
