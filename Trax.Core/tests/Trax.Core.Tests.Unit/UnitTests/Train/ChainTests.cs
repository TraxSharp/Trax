using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ChainTests : TestSetup
{
    #region Internal ChainJunction (junction execution + tuple result)

    [Test]
    public async Task ChainJunction_TJunctionTInTOut_PopulatesMemoryAndReturnsRight()
    {
        var train = new TestTrain().Activate(1);

        var (monad, returnValue) = await train.ChainJunction<TestJunction, string, bool>(
            new TestJunction(),
            "hello"
        );

        returnValue.IsRight.Should().BeTrue();
        returnValue.ValueUnsafe().Should().Be(true);
        monad.Memory.Should().ContainValue(true);
        monad.Exception.Should().BeNull();
    }

    [Test]
    public async Task ChainJunction_PreviousJunctionWasLeft_ShortCircuitsAndPropagates()
    {
        var train = new TestTrain().Activate(1);

        var (_, returnValue) = await train.ChainJunction<TestJunction, string, bool>(
            new TestJunction(),
            new Exception("upstream")
        );

        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Message.Should().Be("upstream");
        train.Exception.Should().NotBeNull();
    }

    [Test]
    public async Task ChainJunction_JunctionThrows_CapturesExceptionInLeft()
    {
        var train = new TestTrain().Activate(1);

        var (_, returnValue) = await train.ChainJunction<TestExceptionJunction, string, bool>(
            new TestExceptionJunction(),
            "hello"
        );

        returnValue.IsLeft.Should().BeTrue();
        returnValue.Swap().ValueUnsafe().Should().BeOfType<NotImplementedException>();
        train.Exception.Should().NotBeNull();
    }

    [Test]
    public async Task ChainJunction_TupleOutput_DecomposesIntoMemory()
    {
        var train = new TestTrain().Activate(1);

        var (monad, _) = await train.ChainJunction<TestTupleOutputJunction, string, (bool, char)>(
            new TestTupleOutputJunction(),
            "hello"
        );

        monad.Memory.Should().ContainValue(true);
        monad.Memory.Should().ContainValue('h');
    }

    [Test]
    public async Task ChainJunction_InputFromMemory_ExtractsAndExecutes()
    {
        var train = new TestTrain().Activate(1, "hello");

        await train.ChainJunction<TestJunction, string, bool>(new TestJunction());

        train.Memory.Should().ContainValue(true);
        train.Exception.Should().BeNull();
    }

    #endregion

    #region Public Chain<TJunction, TIn, TOut>

    [Test]
    public async Task Chain_TJunctionTInTOutWithInstance_RunsJunction()
    {
        var train = new TestTrain().Activate(1, "hello");

        await train.Chain<TestJunction, string, bool>(new TestJunction());

        train.Memory.Should().ContainValue(true);
        train.Exception.Should().BeNull();
    }

    [Test]
    public async Task Chain_TJunctionTInTOutWithDefaultCtor_RunsJunction()
    {
        var train = new TestTrain().Activate(1, "hello");

        await train.Chain<TestJunction, string, bool>();

        train.Memory.Should().ContainValue(true);
        train.Exception.Should().BeNull();
    }

    [Test]
    public async Task Chain_TJunctionTInWithInstance_RunsJunctionForUnitOutput()
    {
        var train = new TestTrain().Activate(1, "hello");

        await train.Chain<TestUnitJunction, string>(new TestUnitJunction());

        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    [Test]
    public async Task Chain_TJunctionTInWithDefaultCtor_RunsJunctionForUnitOutput()
    {
        var train = new TestTrain().Activate(1, "hello");

        await train.Chain<TestUnitJunction, string>();

        train.Memory.Should().NotBeNull();
        train.Exception.Should().BeNull();
    }

    #endregion

    #region Public Chain<TJunction>

    [Test]
    public async Task Chain_OneTypeArgWithInstance_RunsJunction()
    {
        var testJunction = new TestJunction();
        var train = new TestTrain().Activate(1, "hello");

        await train.Chain<TestJunction>(testJunction);

        train.Memory.Should().ContainValue(true);
        train.Exception.Should().BeNull();
    }

    [Test]
    public async Task Chain_OneTypeArgNoInstance_ResolvesFromContainer()
    {
        var train = new TestTrain().Activate(1, "hello");

        await train.Chain<TestJunction>();

        train.Memory.Should().ContainValue(true);
        train.Exception.Should().BeNull();
    }

    #endregion

    #region IChain<TJunction>

    [Test]
    public async Task IChain_RegisteredInterfaceJunction_RunsViaMemory()
    {
        var testJunction = (ITestJunction)new TestJunction();
        var train = new TestTrain().Activate(1, "hello").AddServices(testJunction);

        await train.IChain<ITestJunction>();

        train.Memory.Should().ContainValue(true);
        train.Exception.Should().BeNull();
    }

    [Test]
    public async Task IChain_NonInterfaceTypeArg_RecordsTrainException()
    {
        var train = new TestTrain().Activate(1, "hello");

        await train.IChain<TestJunction>();

        train.Exception.Should().NotBeNull();
    }

    #endregion

    #region Test fixtures

    private class TestTupleOutputJunction : Junction<string, (bool, char)>
    {
        public override async Task<(bool, char)> Run(string input) =>
            (input.Equals("hello"), input.First());
    }

    private class TestExceptionJunction : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => throw new NotImplementedException();
    }

    private interface ITestUnitJunction : IJunction<string, LanguageExt.Unit> { }

    private class TestUnitJunction : Junction<string, LanguageExt.Unit>, ITestUnitJunction
    {
        public override async Task<LanguageExt.Unit> Run(string input) => LanguageExt.Unit.Default;
    }

    private interface ITestJunction : IJunction<string, bool> { }

    private class TestJunction : Junction<string, bool>, ITestJunction
    {
        public override async Task<bool> Run(string input) => input.Equals("hello");
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    #endregion
}
