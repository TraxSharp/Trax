using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// What a train says when a junction, its input or its result cannot be found, and which of
/// those are refused when the chain is read rather than on the first run.
/// </summary>
public class JunctionResolutionMessageTests : TestSetup
{
    [Test]
    public void DeclaredChain_ChainOfAJunctionWithTwoConstructors_IsRefusedNamingItAndTheCount() =>
        new TwoConstructorChainTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(TwoConstructors))
            .And.Contain("2 public constructors");

    [Test]
    public void DeclaredChain_ShortCircuitOfAJunctionWithTwoConstructors_IsRefused() =>
        new TwoConstructorShortCircuitTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(TwoConstructors));

    [Test]
    public void DeclaredChain_ChainOfAJunctionWithNoPublicConstructor_IsRefused() =>
        new NoPublicConstructorTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(NoPublicConstructor))
            .And.Contain("0 public constructors");

    [Test]
    public void DeclaredChain_ChainOfAnInstanceWithTwoConstructors_IsNotRefused() =>
        new TwoConstructorInstanceTrain()
            .DeclaredChain()
            .Refusals.Should()
            .BeEmpty("an instance is already built, so its constructors never run");

    [Test]
    public async Task Run_JunctionWhoseConstructorArgumentIsMissing_NamesTheJunctionTheTypeAndTheFix()
    {
        var message = await FailureMessage(new NeedsClockWithContainerTrain(), "hello");

        message.Should().Contain($"'{nameof(ClockJunction)}'");
        message.Should().Contain($"'{nameof(NeedsClockWithContainerTrain)}'");
        message.Should().Contain($"'{nameof(IClock)}'");
        message.Should().Contain("constructor argument");
        message.Should().Contain("Register it");
        message.Should().Contain("chain a junction that outputs it first");
    }

    [Test]
    public async Task Run_MissingValueInATrainWithNoContainer_DoesNotBlameTheContainer()
    {
        var message = await FailureMessage(new NeedsClockTrain(), "hello");

        message.Should().Contain($"'{nameof(ClockJunction)}'");
        message.Should().Contain($"'{nameof(IClock)}'");
        message.Should().Contain("no container");
        message.Should().Contain("AddServices");
        message.Should().NotContain("registered in the container");
    }

    [Test]
    public async Task Run_MissingClassInATrainWithNoContainer_DoesNotSuggestAddServices()
    {
        var message = await FailureMessage(new MissingInputTrain(), "hello");

        message.Should().Contain("no container");
        message
            .Should()
            .NotContain("AddServices", "AddServices stores a service under an interface only");
    }

    [Test]
    public async Task Run_TupleInputWithAnElementMissing_NamesTheJunctionTheTrainAndTheElement()
    {
        var message = await FailureMessage(new MissingTupleInputTrain(), "hello");

        message.Should().Contain($"'{nameof(NeedsStringAndWidget)}'");
        message.Should().Contain($"'{nameof(MissingTupleInputTrain)}'");
        message.Should().Contain($"'{nameof(Widget)}'");
        message.Should().Contain("Memory only");
        message
            .Should()
            .NotContain(
                "registered in the container",
                "the container is never asked for a tuple element, so registering it would not help"
            );
    }

    [Test]
    public async Task Run_TupleConstructorArgumentWithAnElementMissing_NamesTheJunctionAndTheElement()
    {
        var message = await FailureMessage(new MissingTupleConstructorArgumentTrain(), "hello");

        message.Should().Contain($"'{nameof(TupleConstructorJunction)}'");
        message.Should().Contain($"'{nameof(MissingTupleConstructorArgumentTrain)}'");
        message.Should().Contain($"'{nameof(Widget)}'");
        message.Should().Contain("constructor argument");
    }

    [Test]
    public async Task Run_LoggerConstructorArgumentWithNoFactory_NamesTheJunctionAndHowToSupplyOne()
    {
        var message = await FailureMessage(new NeedsLoggerTrain(), "hello");

        message.Should().Contain($"'{nameof(LoggerJunction)}'");
        message.Should().Contain($"'{nameof(NeedsLoggerTrain)}'");
        message.Should().Contain("ILoggerFactory");
        message.Should().Contain("AddServices");
    }

    [Test]
    public async Task Run_LoggerConstructorArgumentWithAContainerButNoLogging_SaysToRegisterLogging()
    {
        var message = await FailureMessage(new NeedsLoggerWithContainerTrain(), "hello");

        message.Should().Contain($"'{nameof(LoggerJunction)}'");
        message.Should().Contain("Register logging");
    }

    [Test]
    public async Task Run_LoggerConstructorArgumentWithAFactoryPassed_Succeeds()
    {
        var result = await new NeedsLoggerWithFactoryTrain().RunEither("hello");

        result.IsRight.Should().BeTrue(result.IsLeft ? result.Swap().ValueUnsafe().Message : "");
    }

    [Test]
    public async Task Run_JunctionWhoseInputIsMissing_NamesTheJunctionAndTheInput()
    {
        var message = await FailureMessage(new MissingInputTrain(), "hello");

        message.Should().Contain($"'{nameof(NeedsWidget)}'");
        message.Should().Contain($"'{nameof(Widget)}'");
        message.Should().Contain("as its input");
    }

    [Test]
    public async Task Run_ShortCircuitWhoseInputIsMissing_NamesTheJunctionAndTheInput()
    {
        var message = await FailureMessage(new MissingShortCircuitInputTrain(), "hello");

        message.Should().Contain($"'{nameof(NeedsWidget)}'");
        message.Should().Contain($"'{nameof(Widget)}'");
    }

    [Test]
    public async Task Run_IChainWithNoImplementation_NamesTheInterfaceAndSaysHowToSupplyOne()
    {
        var message = await FailureMessage(new MissingIChainTrain(), "hello");

        message.Should().Contain($"'{nameof(IWidgetJunction)}'");
        message.Should().Contain("AddServices");
    }

    [Test]
    public async Task Run_ResolveWithNothingProducingTheResult_NamesTheTrainAndTheResultType()
    {
        var message = await FailureMessage(new ResultNeverProducedTrain(), "hello");

        message.Should().Contain($"'{nameof(ResultNeverProducedTrain)}'");
        message.Should().Contain($"'{nameof(Widget)}'");
        message.Should().Contain("Resolve()");
    }

    [Test]
    public async Task Run_ResolveOfAValueTypeNothingProduced_FailsRatherThanReturningTheDefault()
    {
        var message = await FailureMessage(new ValueResultNeverProducedTrain(), "hello");

        message.Should().Contain($"'{nameof(ValueResultNeverProducedTrain)}'");
        message.Should().Contain("'Guid'");
    }

    [Test]
    public async Task Run_ExtractFromATypeNotInMemory_NamesTheExtractAndTheMissingType()
    {
        var message = await FailureMessage(new ExtractFromMissingTrain(), "hello");

        message.Should().Contain($"Extract<{nameof(Widget)}, String>");
        message.Should().Contain($"'{nameof(Widget)}'");
    }

    [Test]
    public void DeclaredChain_ChainOfAnAbstractJunction_IsRefusedNamingIt() =>
        new AbstractJunctionTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(AbstractJunction))
            .And.Contain("abstract");

    [Test]
    public async Task Run_ResolveOfAValueTypeProducedAsItsDefault_ReturnsTheDefault()
    {
        // A produced default is a value in Memory, not an absence, so it must not read as missing.
        (await Result(new ProducesZeroTrain()))
            .Should()
            .Be(0);
        (await Result(new ProducesFalseTrain())).Should().BeFalse();
        (await Result(new ProducesEmptyGuidTrain())).Should().Be(Guid.Empty);
    }

    private static async Task<TOut> Result<TOut>(Train<string, TOut> train)
    {
        var result = await train.RunEither("hello");

        result.IsRight.Should().BeTrue(result.IsLeft ? result.Swap().ValueUnsafe().Message : "");

        return result.ValueUnsafe();
    }

    private static async Task<string> FailureMessage<TOut>(Train<string, TOut> train, string input)
    {
        var result = await train.RunEither(input);

        result.IsLeft.Should().BeTrue();
        var exception = result.Swap().ValueUnsafe();
        exception.Should().BeOfType<TrainException>();

        return exception.Message;
    }

    public interface IClock;

    /// <summary>A container that registers nothing.</summary>
    private sealed class EmptyContainer : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private abstract class WithEmptyContainer<TOut> : Train<string, TOut>
    {
        protected override Monad<string, TOut> NewMonad() =>
            new(this, new EmptyContainer(), CancellationToken);
    }

    private abstract class AbstractJunction : Junction<string, int>;

    private class NeedsStringAndWidget : Junction<(string, Widget), int>
    {
        public override Task<int> Run((string, Widget) input) => Task.FromResult(1);
    }

    private class TupleConstructorJunction((string, Widget) pair) : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(pair.Item1.Length);
    }

    private class LoggerJunction(ILogger<LoggerJunction> logger) : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(logger.GetHashCode());
    }

    private class Zero : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(0);
    }

    private class False : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(false);
    }

    private class EmptyGuid : Junction<string, Guid>
    {
        public override Task<Guid> Run(string input) => Task.FromResult(Guid.Empty);
    }

    private class NeedsClockWithContainerTrain : WithEmptyContainer<int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<ClockJunction>().Resolve();
    }

    private class MissingTupleInputTrain : WithEmptyContainer<int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<NeedsStringAndWidget>().Resolve();
    }

    private class MissingTupleConstructorArgumentTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<TupleConstructorJunction>().Resolve();
    }

    private class NeedsLoggerTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<LoggerJunction>().Resolve();
    }

    private class NeedsLoggerWithContainerTrain : WithEmptyContainer<int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<LoggerJunction>().Resolve();
    }

    private class NeedsLoggerWithFactoryTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            AddServices<ILoggerFactory>(NullLoggerFactory.Instance)
                .Chain<LoggerJunction>()
                .Resolve();
    }

    private class AbstractJunctionTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<AbstractJunction>().Resolve();
    }

    private class ProducesZeroTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() => Chain<Zero>().Resolve();
    }

    private class ProducesFalseTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() => Chain<False>().Resolve();
    }

    private class ProducesEmptyGuidTrain : Train<string, Guid>
    {
        protected override Task<Either<Exception, Guid>> Junctions() =>
            Chain<EmptyGuid>().Resolve();
    }

    public class Widget;

    public interface IWidgetJunction : IJunction<string, int>;

    private class ClockJunction(IClock clock) : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(clock.GetHashCode());
    }

    private class TwoConstructors : Junction<string, int>
    {
        public TwoConstructors() { }

        public TwoConstructors(IClock clock) => _ = clock;

        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class NoPublicConstructor : Junction<string, int>
    {
        private NoPublicConstructor() { }

        public static NoPublicConstructor Create() => new();

        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class NeedsWidget : Junction<Widget, int>
    {
        public override Task<int> Run(Widget input) => Task.FromResult(1);
    }

    private class NeedsClockTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<ClockJunction>().Resolve();
    }

    private class TwoConstructorChainTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<TwoConstructors>().Resolve();
    }

    private class TwoConstructorShortCircuitTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            ShortCircuit<TwoConstructors>().Resolve();
    }

    private class NoPublicConstructorTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<NoPublicConstructor>().Resolve();
    }

    private class TwoConstructorInstanceTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain(new TwoConstructors()).Resolve();
    }

    private class MissingInputTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<NeedsWidget>().Resolve();
    }

    private class MissingShortCircuitInputTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            ShortCircuit<NeedsWidget>().Resolve();
    }

    private class MissingIChainTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            IChain<IWidgetJunction>().Resolve();
    }

    private class ResultNeverProducedTrain : Train<string, Widget>
    {
        protected override Task<Either<Exception, Widget>> Junctions() =>
            Task.FromResult(Resolve());
    }

    private class ValueResultNeverProducedTrain : Train<string, Guid>
    {
        protected override Task<Either<Exception, Guid>> Junctions() => Task.FromResult(Resolve());
    }

    private class ExtractFromMissingTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult(Extract<Widget, string>().Resolve());
    }
}
