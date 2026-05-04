using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ChainShortCircuitEdgeCaseTests : TestSetup
{
    [Test]
    public async Task ChainJunction_ExceptionAlreadySet_ReturnsEarly()
    {
        var monad = new TestTrain().Activate(0);
        monad.Exception = new TrainException("preset");

        var (returned, result) = await monad.ChainJunction<EchoJunction, string, string>(
            new EchoJunction(),
            "x"
        );

        returned.Should().BeSameAs(monad);
        result.IsLeft.Should().BeTrue();
    }

    [Test]
    public async Task ChainJunctionFromMemory_InputMissing_ReturnsEarlyWithException()
    {
        var monad = new TestTrain().Activate(0);

        var returned = await monad.ChainJunction<EchoJunction, string, string>(new EchoJunction());

        returned.Should().BeSameAs(monad);
        monad.Exception.Should().NotBeNull();
    }

    [Test]
    public async Task ChainAsync_ExceptionAlreadySet_DoesNotRunJunction()
    {
        var monad = new TestTrain().Activate(0);
        monad.Exception = new TrainException("preset");

        var either = await monad.Chain<EchoJunction>().Resolve();

        either.IsLeft.Should().BeTrue();
        either.Swap().ValueUnsafe().Message.Should().Be("preset");
    }

    [Test]
    public async Task ChainAsync_InitializeJunctionReturnsNull_ReturnsEarly()
    {
        var monad = new TestTrain().Activate(0);

        // MultiCtorJunction has multiple constructors → InitializeJunction returns null.
        var either = await monad.Chain<MultiCtorJunction>().Resolve();

        either.IsLeft.Should().BeTrue();
    }

    [Test]
    public async Task IChainAsync_InterfaceNotInMemory_ReturnsEarly()
    {
        var monad = new TestTrain().Activate(0);

        var either = await monad.IChain<IUnregistered>().Resolve();

        either.IsLeft.Should().BeTrue();
    }

    [Test]
    public async Task ShortCircuitJunction_ExceptionAlreadySet_ReturnsEarly()
    {
        var monad = new TestTrain().Activate(0);
        monad.Exception = new TrainException("preset");

        var (returned, result) = await monad.ShortCircuitJunction<EchoJunction, string, string>(
            new EchoJunction(),
            "x"
        );

        returned.Should().BeSameAs(monad);
        result.IsLeft.Should().BeTrue();
    }

    [Test]
    public async Task ShortCircuitAsync_InitializeJunctionReturnsNull_ReturnsEarly()
    {
        var monad = new TestTrain().Activate(0);

        var either = await monad.ShortCircuit<MultiCtorJunction>().Resolve();

        either.IsLeft.Should().BeTrue();
    }

    private interface IUnregistered : IJunction<string, string> { }

    private class EchoJunction : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    private class MultiCtorJunction
    {
        public MultiCtorJunction() { }

        public MultiCtorJunction(int x) { }
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
