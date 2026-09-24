using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ResolveTests : TestSetup
{
    [Theory]
    public async Task TestResolvePrimitive()
    {
        // Arrange
        var input = 1;
        var train = new TestTrain().Activate(input);

        // Act
        var result = train.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(1);
    }

    [Theory]
    public async Task TestResolveObject()
    {
        // Arrange
        var input = new object();
        var train = new TestObjectTrain().Activate(input);

        // Act
        var result = train.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(input);
    }

    [Theory]
    public async Task TestResolveTuple()
    {
        // Arrange
        var intInput = 1;
        var stringInput = "string";
        var train = new TestTupleTrain().Activate(LanguageExt.Unit.Default, intInput, stringInput);

        // Act
        var result = train.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be((intInput, stringInput));
    }

    [Theory]
    public async Task TestResolveShortCircuitValueSet()
    {
        // Arrange
        var input = 1;
        var monadTask = new TestStringTrain()
            .Activate(input)
            .ShortCircuit<TestShortCircuitJunction>();

        // Act
        var result = await monadTask.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be(input.ToString());
    }

    [Theory]
    public async Task TestInvalidResolve()
    {
        // Arrange
        var input = 1;
        var train = new TestStringTrain().Activate(input);

        // Act
        var result = train.Resolve();

        // Assert
        result.Should().NotBeNull();
        result.IsLeft.Should().BeTrue();
        result.Swap().ValueUnsafe().Should().BeOfType<TrainException>();
    }

    [Test]
    public async Task Resolve_AValueAfterAFailedJunction_ReturnsTheFailure()
    {
        var result = await new TestStringTrain()
            .Activate(1)
            .Chain<FailingJunction>()
            .Resolve("stated result");

        result.IsLeft.Should().BeTrue("a stated result must not hide a failure before it");
        result.Swap().ValueUnsafe().Message.Should().Be(FailingJunction.Failure);
    }

    [Test]
    public async Task Resolve_AValueAfterACleanChain_ReturnsTheValue()
    {
        var result = await new TestStringTrain()
            .Activate(1)
            .Chain<TestShortCircuitJunction>()
            .Resolve("stated result");

        result.IsRight.Should().BeTrue();
        result.ValueUnsafe().Should().Be("stated result");
    }

    private class TestTrain : Train<int, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            throw new NotImplementedException();
    }

    private class TestStringTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            throw new NotImplementedException();
    }

    private class TestObjectTrain : Train<object, object>
    {
        protected override Task<Either<Exception, object>> Junctions() =>
            throw new NotImplementedException();
    }

    private class TestTupleTrain : Train<LanguageExt.Unit, (int, string)>
    {
        protected override Task<Either<Exception, (int, string)>> Junctions() =>
            throw new NotImplementedException();
    }

    private class TestShortCircuitJunction : Junction<int, string>
    {
        public override async Task<string> Run(int input) => input.ToString();
    }

    private class FailingJunction : Junction<int, string>
    {
        public const string Failure = "the junction failed";

        public override Task<string> Run(int input) => throw new InvalidOperationException(Failure);
    }
}
