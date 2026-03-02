using FluentAssertions;
using LanguageExt;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ActivateTests : TestSetup
{
    [Theory]
    public async Task TestActivateTests()
    {
        // Arrange
        var input = new object();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(input);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Memory.Count.Should().Be(2);
        monad.Memory.Should().ContainValue(input);
        monad.Exception.Should().BeNull();
    }

    [Theory]
    public async Task TestInvalidActivateTests()
    {
        // Arrange
        object input = null!;
        var train = new TestTrain();

        // Act
        var monad = train.Activate(input);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Exception.Should().NotBeNull();
    }

    [Theory]
    public async Task TestActivateTestsParamsInput()
    {
        // Arrange
        var input = new object();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(input, 1, "hello", false);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Memory.Count.Should().Be(48);
        monad.Memory.Should().ContainValue(input);
        monad.Memory.Should().ContainValue(1);
        monad.Memory.Should().ContainValue(false);
        monad.Memory.Should().ContainValue("hello");
        monad.Exception.Should().BeNull();
    }

    [Theory]
    public async Task TestActivateTestsTupleInput()
    {
        // Arrange
        var inputObject = new object();
        var input = (1, inputObject);
        var train = new TestTupleTrain();

        // Act
        var monad = train.Activate(input, 2, "hello", false);

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Memory.Count.Should().Be(48);
        monad.Memory.Should().ContainValue(inputObject);
        monad.Memory.Should().ContainValue(2);
        monad.Memory.Should().ContainValue(false);
        monad.Memory.Should().ContainValue("hello");
        monad.Exception.Should().BeNull();
    }

    [Theory]
    public async Task TestActivateTestsParamsTupleInput()
    {
        // Arrange
        var input = new object();
        var train = new TestTrain();

        // Act
        var monad = train.Activate(input, (1, "hello", false));

        // Assert
        monad.Memory.Should().NotBeNull();
        monad.Memory.Count.Should().Be(48);
        monad.Memory.Should().ContainValue(input);
        monad.Memory.Should().ContainValue(1);
        monad.Memory.Should().ContainValue(false);
        monad.Memory.Should().ContainValue("hello");
        monad.Exception.Should().BeNull();
    }

    private class TestTrain : Train<object, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(object input) =>
            throw new NotImplementedException();
    }

    private class TestTupleTrain : Train<(int, object), string>
    {
        protected override Task<Either<Exception, string>> RunInternal((int, object) input) =>
            throw new NotImplementedException();
    }
}
