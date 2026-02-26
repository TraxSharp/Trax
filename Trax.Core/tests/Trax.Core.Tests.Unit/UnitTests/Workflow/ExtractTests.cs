using Trax.Core.Monad;
using Trax.Core.Train;
using FluentAssertions;
using LanguageExt;

namespace Trax.Core.Tests.Unit.UnitTests.Workflow;

public class ExtractTests : TestSetup
{
    [Theory]
    public async Task TestExtract()
    {
        // Arrange
        var input = 1;
        var inputObject = new TestClass() { TestString = "hello" };

        var workflow = new TestWorkflow();

        // Act
        var monad = workflow.Activate(input, inputObject);
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

        var workflow = new TestWorkflow();

        // Act
        var monad = workflow.Activate(input);
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

        var workflow = new TestWorkflow();

        // Act
        var monad = workflow.Activate(input, inputObject);
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

        var workflow = new TestWorkflow();

        // Act
        var monad = workflow.Activate(input);
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
        var workflow = new TestWorkflow();

        // Act
        var monad = workflow.Activate(input);
        monad.Extract<TestClass, string>(null!);

        // Assert
        monad.Exception.Should().NotBeNull();
        monad.Memory.Should().NotBeNull();
    }

    private class TestClass
    {
        public string TestString { get; set; }
    }

    private class TestWorkflow : Train<int, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(int input) =>
            throw new NotImplementedException();
    }
}
