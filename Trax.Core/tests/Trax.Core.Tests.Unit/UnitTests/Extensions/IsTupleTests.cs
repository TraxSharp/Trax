using System.Runtime.CompilerServices;
using FluentAssertions;
using Trax.Core.Extensions;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class IsTupleTests : TestSetup
{
    [Theory]
    public async Task TestBuiltInIsTuple()
    {
        // Arrange
        var builtInTuple = ("hello", false).GetType();

        // Act
        var result = builtInTuple.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestValueTupleIsTuple()
    {
        // Arrange
        var valueTuple = new ValueTuple<int>().GetType();

        // Act
        var result = valueTuple.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestITupleIsTuple()
    {
        // Arrange
        var iTuple = ((ITuple)("hello", false)).GetType();

        // Act
        var result = iTuple.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestInvalidIsTuple()
    {
        // Arrange
        var notATuple = new object().GetType();

        // Act
        var result = notATuple.IsTuple();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    public async Task TestNestedTupleIsTuple()
    {
        // Arrange
        var nestedTuple = ((1, ("hello", false))).GetType();

        // Act
        var result = nestedTuple.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestTupleWithMoreThanSevenElementsIsTuple()
    {
        // Arrange
        var longTuple = (1, 2, 3, 4, 5, 6, 7, 8).GetType();

        // Act
        var result = longTuple.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestTupleWithGenericTypeIsTuple()
    {
        // Arrange
        var genericTuple = (new List<int>(), new Dictionary<string, int>()).GetType();

        // Act
        var result = genericTuple.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestTupleWithNullElementIsTuple()
    {
        // Arrange
        var tupleWithNull = ((string)null!, 42).GetType();

        // Act
        var result = tupleWithNull.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestClassWithTupleFieldIsNotTuple()
    {
        // Arrange
        var classWithTupleField = typeof(ClassWithTupleField);

        // Act
        var result = classWithTupleField.IsTuple();

        // Assert
        result.Should().BeFalse();
    }

    private class ClassWithTupleField
    {
        public (int, string) TupleField = default;
    }

    [Theory]
    public async Task TestTupleInArrayIsTuple()
    {
        // Arrange
        var tupleArray = new (int, string)[] { (1, "a"), (2, "b") }.GetType();

        // Act
        var result = tupleArray.IsTuple();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    public async Task TestTupleTypeIsTuple()
    {
        // Arrange
        var tupleType = typeof((int, string));

        // Act
        var result = tupleType.IsTuple();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    public async Task TestTupleInsideGenericTypeIsNotTuple()
    {
        // Arrange
        var listOfTuples = new List<(int, string)>().GetType();

        // Act
        var result = listOfTuples.IsTuple();

        // Assert
        result.Should().BeFalse();
    }
}
