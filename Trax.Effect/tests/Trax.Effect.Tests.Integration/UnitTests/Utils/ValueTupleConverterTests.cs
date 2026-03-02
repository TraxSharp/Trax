using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

[TestFixture]
public class ValueTupleConverterTests
{
    private JsonSerializerOptions _options;

    [SetUp]
    public void SetUp()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new ValueTupleConverter());
    }

    [Test]
    public void CanConvert_ValueTupleType_ReturnsTrue()
    {
        // Arrange
        var converter = new ValueTupleConverter();

        // Act
        var result = converter.CanConvert(typeof((int, string)));

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void CanConvert_NonTupleType_ReturnsFalse()
    {
        // Arrange
        var converter = new ValueTupleConverter();

        // Act
        var result = converter.CanConvert(typeof(int));

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void Serialize_Tuple2_ProducesJsonArray()
    {
        // Arrange
        var tuple = (1, "hello");

        // Act
        var json = JsonSerializer.Serialize(tuple, _options);

        // Assert
        json.Should().Be("[1,\"hello\"]");
    }

    [Test]
    public void Serialize_Tuple3_ProducesJsonArray()
    {
        // Arrange
        var tuple = (1, 2, 3.0);

        // Act
        var json = JsonSerializer.Serialize(tuple, _options);

        // Assert
        json.Should().Be("[1,2,3]");
    }

    [Test]
    public void CanConvert_NestedTupleType_ReturnsTrue()
    {
        // Arrange
        var converter = new ValueTupleConverter();

        // Act
        var result = converter.CanConvert(typeof((int, int, int)));

        // Assert
        result.Should().BeTrue();
    }
}
