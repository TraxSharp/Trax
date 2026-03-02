using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Step;
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
        var train = new TestStringTrain().Activate(input).ShortCircuit<TestShortCircuitStep>();

        // Act
        var result = train.Resolve();

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

    private class TestTrain : Train<int, int>
    {
        protected override Task<Either<Exception, int>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    private class TestStringTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }

    private class TestObjectTrain : Train<object, object>
    {
        protected override Task<Either<Exception, object>> RunInternal(object input) =>
            throw new NotImplementedException();
    }

    private class TestTupleTrain : Train<LanguageExt.Unit, (int, string)>
    {
        protected override Task<Either<Exception, (int, string)>> RunInternal(
            LanguageExt.Unit input
        ) => throw new NotImplementedException();
    }

    private class TestShortCircuitStep : Step<int, string>
    {
        public override async Task<string> Run(int input) => input.ToString();
    }
}
