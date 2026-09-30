using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
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
        var message = await FailureMessage(new NeedsClockTrain(), "hello");

        message.Should().Contain($"'{nameof(ClockJunction)}'");
        message.Should().Contain($"'{nameof(NeedsClockTrain)}'");
        message.Should().Contain($"'{nameof(IClock)}'");
        message.Should().Contain("constructor argument");
        message.Should().Contain("Register it");
        message.Should().Contain("chain a junction that outputs it first");
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

    private static async Task<string> FailureMessage<TOut>(Train<string, TOut> train, string input)
    {
        var result = await train.RunEither(input);

        result.IsLeft.Should().BeTrue();
        var exception = result.Swap().ValueUnsafe();
        exception.Should().BeOfType<TrainException>();

        return exception.Message;
    }

    public interface IClock;

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
