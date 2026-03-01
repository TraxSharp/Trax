using FluentAssertions;
using Trax.Core.Exceptions;

namespace Trax.Core.Tests.Unit.UnitTests.Utils;

public class ExtractTypeTuplesTests : TestSetup
{
    [Theory]
    public async Task TestValidExtractTypeTuples()
    {
        // Arrange
        var memory = new Dictionary<Type, object>();
        memory[typeof(int)] = 1;
        memory[typeof(string)] = "hello";
        memory[typeof(bool)] = false;

        var inputType = typeof(ValueTuple<int, string, bool>);

        // Act
        var result = Trax.Core.Utils.TypeHelpers.ExtractTypeTuples(memory, inputType);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(3);
        result.Should().Contain((typeof(int), 1));
        result.Should().Contain((typeof(string), "hello"));
        result.Should().Contain((typeof(bool), false));
    }

    [Theory]
    public async Task TestInvalidExtractTypeTuples()
    {
        // Arrange
        var memory = new Dictionary<Type, object>();
        memory[typeof(int)] = 1;
        memory[typeof(string)] = "hello";

        var inputType = typeof(ValueTuple<int, string, bool>);

        // Act
        Assert.Throws<WorkflowException>(() =>
            Trax.Core.Utils.TypeHelpers.ExtractTypeTuples(memory, inputType)
        );
    }
}
