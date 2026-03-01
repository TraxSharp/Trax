using FluentAssertions;
using Trax.Core.Exceptions;

namespace Trax.Core.Tests.Unit.UnitTests.Utils;

public class ConvertTupleTests : TestSetup
{
    [Theory]
    public async Task TestConvertTwoTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>() { (typeof(int), 1), (typeof(bool), false) };

        // Act
        var result =
            (ValueTuple<int, bool>)Trax.Core.Utils.TypeHelpers.ConvertTwoTuple(listToConvert);

        // Assert
        result.Item1.Should().Be(1);
        result.Item2.Should().Be(false);
    }

    [Theory]
    public async Task TestInvalidConvertTwoTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>() { (typeof(int), 1) };

        // Act
        Assert.Throws<ArgumentException>(() =>
            Trax.Core.Utils.TypeHelpers.ConvertTwoTuple(listToConvert)
        );
    }

    [Theory]
    public async Task TestConvertThreeTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
        };

        // Act
        var result =
            (ValueTuple<int, bool, string>)
                Trax.Core.Utils.TypeHelpers.ConvertThreeTuple(listToConvert);

        // Assert
        result.Item1.Should().Be(1);
        result.Item2.Should().Be(false);
        result.Item3.Should().Be("test");
    }

    [Theory]
    public async Task TestInvalidConvertThreeTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>() { (typeof(int), 1), (typeof(bool), false) };

        // Act
        Assert.Throws<ArgumentException>(() =>
            Trax.Core.Utils.TypeHelpers.ConvertThreeTuple(listToConvert)
        );
    }

    [Theory]
    public async Task TestConvertFourTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
            (typeof(double), 2.0),
        };

        // Act
        var result =
            (ValueTuple<int, bool, string, double>)
                Trax.Core.Utils.TypeHelpers.ConvertFourTuple(listToConvert);

        // Assert
        result.Item1.Should().Be(1);
        result.Item2.Should().Be(false);
        result.Item3.Should().Be("test");
        result.Item4.Should().Be(2.0);
    }

    [Theory]
    public async Task TestInvalidConvertFourTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
        };

        // Act
        Assert.Throws<ArgumentException>(() =>
            Trax.Core.Utils.TypeHelpers.ConvertFourTuple(listToConvert)
        );
    }

    [Theory]
    public async Task TestConvertFiveTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
            (typeof(double), 2.0),
            (typeof(float), 3.0f),
        };

        // Act
        var result =
            (ValueTuple<int, bool, string, double, float>)
                Trax.Core.Utils.TypeHelpers.ConvertFiveTuple(listToConvert);

        // Assert
        result.Item1.Should().Be(1);
        result.Item2.Should().Be(false);
        result.Item3.Should().Be("test");
        result.Item4.Should().Be(2.0);
        result.Item5.Should().Be(3.0f);
    }

    [Theory]
    public async Task TestInvalidConvertFiveTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
            (typeof(double), 2.0),
        };

        // Act
        Assert.Throws<ArgumentException>(() =>
            Trax.Core.Utils.TypeHelpers.ConvertFiveTuple(listToConvert)
        );
    }

    [Theory]
    public async Task TestConvertSixTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
            (typeof(double), 2.0),
            (typeof(float), 3.0f),
            (typeof(decimal), 4.0m),
        };

        // Act
        var result =
            (ValueTuple<int, bool, string, double, float, decimal>)
                Trax.Core.Utils.TypeHelpers.ConvertSixTuple(listToConvert);

        // Assert
        result.Item1.Should().Be(1);
        result.Item2.Should().Be(false);
        result.Item3.Should().Be("test");
        result.Item4.Should().Be(2.0);
        result.Item5.Should().Be(3.0f);
        result.Item6.Should().Be(4.0m);
    }

    [Theory]
    public async Task TestInvalidConvertSixTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
            (typeof(double), 2.0),
            (typeof(float), 3.0f),
        };

        // Act
        Assert.Throws<ArgumentException>(() =>
            Trax.Core.Utils.TypeHelpers.ConvertSixTuple(listToConvert)
        );
    }

    [Theory]
    public async Task TestConvertSevenTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
            (typeof(double), 2.0),
            (typeof(float), 3.0f),
            (typeof(decimal), 4.0m),
            (typeof(char), 'a'),
        };

        // Act
        var result =
            (ValueTuple<int, bool, string, double, float, decimal, char>)
                Trax.Core.Utils.TypeHelpers.ConvertSevenTuple(listToConvert);

        // Assert
        result.Item1.Should().Be(1);
        result.Item2.Should().Be(false);
        result.Item3.Should().Be("test");
        result.Item4.Should().Be(2.0);
        result.Item5.Should().Be(3.0f);
        result.Item6.Should().Be(4.0m);
        result.Item7.Should().Be('a');
    }

    [Theory]
    public async Task TestInvalidConvertSevenTuple()
    {
        // Arrange
        var listToConvert = new List<(Type, dynamic)>()
        {
            (typeof(int), 1),
            (typeof(bool), false),
            (typeof(string), "test"),
            (typeof(double), 2.0),
            (typeof(float), 3.0f),
            (typeof(decimal), 4.0m),
        };

        // Act
        Assert.Throws<ArgumentException>(() =>
            Trax.Core.Utils.TypeHelpers.ConvertSevenTuple(listToConvert)
        );
    }
}
