using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class JunctionsTests : TestSetup
{
    #region Junctions — happy path

    [Test]
    public async Task Junctions_SingleChain_ReturnsOutput()
    {
        var train = new SingleChainTrain();

        var result = await train.Run("hello");

        result.Should().Be(5);
    }

    [Test]
    public async Task Junctions_MultipleChains_ReturnsOutput()
    {
        var train = new MultiChainTrain();

        var result = await train.Run("hello");

        result.Should().Be(true);
    }

    [Test]
    public async Task Junctions_RunEither_ReturnsRight()
    {
        var train = new SingleChainTrain();

        var result = await train.RunEither("hello");

        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(5);
    }

    #endregion

    #region Junctions — failure path

    [Test]
    public async Task Junctions_JunctionThrows_RunEitherReturnsLeft()
    {
        var train = new ThrowingChainTrain();

        var result = await train.RunEither("hello");

        result.IsLeft.Should().BeTrue();
        result.Swap().ValueUnsafe().Message.Should().Contain("junction failed");
    }

    [Test]
    public async Task Junctions_JunctionThrows_RunThrows()
    {
        var train = new ThrowingChainTrain();

        var act = async () => await train.Run("hello");

        await act.Should().ThrowAsync<Exception>().WithMessage("*junction failed*");
    }

    [Test]
    public async Task Junctions_MissingReturnType_RunEitherReturnsLeft()
    {
        var train = new MissingRefReturnTypeTrain();

        var result = await train.RunEither("hello");

        result.IsLeft.Should().BeTrue();
        result.Swap().ValueUnsafe().Should().BeOfType<TrainException>();
    }

    #endregion

    #region Junctions — Extract

    [Test]
    public async Task Junctions_Extract_ExtractsFromMemory()
    {
        var train = new ExtractTrain();

        var result = await train.Run(new Wrapper("extracted"));

        result.Should().Be("extracted");
    }

    #endregion

    #region Backwards compatibility

    [Test]
    public async Task RunInternal_Override_StillWorks()
    {
        var train = new RunInternalTrain();

        var result = await train.Run("hello");

        result.Should().Be(5);
    }

    [Test]
    public async Task RunInternal_RunEither_StillReturnsEither()
    {
        var train = new RunInternalTrain();

        var result = await train.RunEither("hello");

        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(5);
    }

    #endregion

    #region Implicit conversion

    [Test]
    public async Task ImplicitConversion_OnSuccess_ReturnsTReturn()
    {
        var train = new SingleChainTrain();
        var monad = new Monad.Monad<string, int>(train, CancellationToken.None).Activate("hello");

        monad.Chain<StringLengthJunction>();

        int result = monad;

        result.Should().Be(5);
    }

    [Test]
    public void ImplicitConversion_OnException_Throws()
    {
        var train = new SingleChainTrain();
        var monad = new Monad.Monad<string, int>(train, CancellationToken.None).Activate("hello");

        monad.Chain<ThrowingJunction>();

        var act = () =>
        {
            int result = monad;
        };

        act.Should().Throw<Exception>().WithMessage("*junction failed*");
    }

    [Test]
    public void ImplicitConversion_MissingType_ThrowsTrainException()
    {
        var train = new MissingRefReturnTypeTrain();
        var monad = new Monad.Monad<string, List<string>>(train, CancellationToken.None).Activate(
            "hello"
        );

        // Don't chain anything — TReturn (List<string>) won't be in memory
        var act = () =>
        {
            List<string> result = monad;
        };

        act.Should().Throw<TrainException>();
    }

    #endregion

    #region Fakes

    private class StringLengthJunction : Junction<string, int>
    {
        public override async Task<int> Run(string input) => input.Length;
    }

    private class IntToBoolJunction : Junction<int, bool>
    {
        public override async Task<bool> Run(int input) => input > 0;
    }

    private class ThrowingJunction : Junction<string, int>
    {
        public override Task<int> Run(string input) =>
            throw new InvalidOperationException("junction failed");
    }

    private class IdentityJunction : Junction<string, string>
    {
        public override async Task<string> Run(string input) => input;
    }

    private record Wrapper(string Value);

    private class SingleChainTrain : Train<string, int>
    {
        protected override int Junctions() => Chain<StringLengthJunction>();
    }

    private class MultiChainTrain : Train<string, bool>
    {
        protected override bool Junctions() =>
            Chain<StringLengthJunction>().Chain<IntToBoolJunction>();
    }

    private class ThrowingChainTrain : Train<string, int>
    {
        protected override int Junctions() => Chain<ThrowingJunction>();
    }

    private class MissingRefReturnTypeTrain : Train<string, List<string>>
    {
        protected override List<string> Junctions() => Chain<IdentityJunction>();
    }

    private class ExtractTrain : Train<Wrapper, string>
    {
        protected override string Junctions() => Extract<Wrapper, string>();
    }

    private class RunInternalTrain : Train<string, int>
    {
        protected override async Task<Either<Exception, int>> RunInternal(string input) =>
            Activate(input).Chain<StringLengthJunction>().Resolve();
    }

    #endregion
}
