using FluentAssertions;
using LanguageExt;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

public class ExtractTests : TestSetup
{
    [Theory]
    public async Task TestExtract()
    {
        // Arrange
        var input = 1;
        var inputObject = new TestClass() { TestString = "hello" };

        var train = new TestTrain();

        // Act
        var monad = train.Activate(input, inputObject);
        monad.Extract<TestClass, string>();

        // Assert
        monad.Exception.Should().BeNull();
        monad.Memory.Should().NotBeNull();
        monad.Memory.Should().ContainValue("hello");
    }

    [Theory]
    public async Task TestExtractTypeInput()
    {
        // Arrange
        var input = 1;
        var inputObject = new TestClass() { TestString = "hello" };

        var train = new TestTrain();

        // Act
        var monad = train.Activate(input);
        monad.Extract<TestClass, string>(inputObject);

        // Assert
        monad.Exception.Should().BeNull();
        monad.Memory.Should().NotBeNull();
        monad.Memory.Should().ContainValue("hello");
    }

    [Theory]
    public async Task TestInvalidExtract()
    {
        // Arrange
        var input = 1;
        var inputObject = new TestClass() { TestString = "hello" };

        var train = new TestTrain();

        // Act
        var monad = train.Activate(input, inputObject);
        monad.Extract<TestClass, bool>();

        // Assert
        monad.Exception.Should().NotBeNull();
        monad.Memory.Should().NotBeNull();
    }

    [Theory]
    public async Task TestInvalidExtractNotInMemory()
    {
        // Arrange
        var input = 1;

        var train = new TestTrain();

        // Act
        var monad = train.Activate(input);
        monad.Extract<TestClass, bool>();

        // Assert
        monad.Exception.Should().NotBeNull();
        monad.Memory.Should().NotBeNull();
    }

    [Theory]
    public async Task TestInvalidExtractTypeInputNull()
    {
        // Arrange
        var input = 1;
        var train = new TestTrain();

        // Act
        var monad = train.Activate(input);
        monad.Extract<TestClass, string>(null!);

        // Assert
        monad.Exception.Should().NotBeNull();
        monad.Memory.Should().NotBeNull();
    }

    private class TestClass
    {
        public string TestString { get; set; } = null!;
    }

    private class TestTrain : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
