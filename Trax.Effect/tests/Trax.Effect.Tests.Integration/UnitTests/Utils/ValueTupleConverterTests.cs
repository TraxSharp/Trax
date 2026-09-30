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

    [Test]
    public void Deserialize_WrongLength_Throws()
    {
        var act = () => JsonSerializer.Deserialize<(int, int)>("[1,2,3]", _options);

        act.Should().Throw<JsonException>().WithMessage("*Expected 2*got 3*");
    }

    [Test]
    public void Deserialize_PrimitiveValues_ReadsWhatSerializeWrote()
    {
        var json = JsonSerializer.Serialize((1, "hello"), _options);

        var tuple = JsonSerializer.Deserialize<(int, string)>(json, _options);

        tuple.Should().Be((1, "hello"));
    }

    [Test]
    public void Deserialize_MixedNumericTypes_ConvertsEachElementToItsFieldType()
    {
        var tuple = JsonSerializer.Deserialize<(int, long, double, decimal)>(
            "[1, 2, 3.5, 4.25]",
            _options
        );

        tuple.Should().Be((1, 2L, 3.5, 4.25m));
    }

    [Test]
    public void Deserialize_ComplexElements_RoundTrip()
    {
        var original = (new List<int> { 1, 2, 3 }, new Point(4, 5), (string?)null);
        var json = JsonSerializer.Serialize(original, _options);

        var tuple = JsonSerializer.Deserialize<(List<int>, Point, string?)>(json, _options);

        tuple.Item1.Should().Equal(1, 2, 3);
        tuple.Item2.Should().Be(new Point(4, 5));
        tuple.Item3.Should().BeNull();
    }

    [Test]
    public void Deserialize_WithTheDefaultTraxOptions_ReadsWhatSerializeWrote()
    {
        // The default options preserve references, so the array is written inside a
        // {"$id": …, "$values": […]} wrapper. A stored train input is written this way.
        var options = TraxJsonSerializationOptions.Default;
        var json = JsonSerializer.Serialize((7, "seven"), options);

        var tuple = JsonSerializer.Deserialize<(int, string)>(json, options);

        tuple.Should().Be((7, "seven"));
    }

    [Test]
    public void Deserialize_NotAnArray_Throws()
    {
        var act = () => JsonSerializer.Deserialize<(int, int)>("42", _options);

        act.Should().Throw<JsonException>();
    }

    [Test]
    public void Deserialize_AnArrayInsideAPreservedReferenceWrapper_ReadsTheArray()
    {
        var tuple = JsonSerializer.Deserialize<(int, string)>(
            """{"$id":"1","$values":[7,"seven"]}""",
            _options
        );

        tuple.Should().Be((7, "seven"));
    }

    [Test]
    public void Deserialize_AnObjectWithoutValues_Throws()
    {
        var act = () => JsonSerializer.Deserialize<(int, int)>("""{"a":1,"b":2}""", _options);

        act.Should().Throw<JsonException>().WithMessage("*Expected a JSON array*Object*");
    }

    [Test]
    public void A_tuple_holding_the_same_object_twice_round_trips_under_Preserve()
    {
        var options = new JsonSerializerOptions
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
        };
        options.Converters.Add(new ValueTupleConverter());
        var box = new Box { Name = "shared" };

        var json = JsonSerializer.Serialize((box, box), options);
        var tuple = JsonSerializer.Deserialize<(Box, Box)>(json, options);

        tuple.Item1.Name.Should().Be("shared");
        tuple.Item2.Name.Should().Be("shared");
    }

    [Test]
    public void A_tuple_holding_an_object_its_parent_also_holds_round_trips_under_Preserve()
    {
        var options = new JsonSerializerOptions
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
        };
        options.Converters.Add(new ValueTupleConverter());
        var box = new Box { Name = "shared" };

        var json = JsonSerializer.Serialize(new Holder { Box = box, Pair = (box, 1) }, options);
        var holder = JsonSerializer.Deserialize<Holder>(json, options)!;

        holder.Box!.Name.Should().Be("shared");
        holder.Pair.Item1.Name.Should().Be("shared");
    }

    public sealed record Point(int X, int Y);

    public sealed class Box
    {
        public string? Name { get; set; }
    }

    public sealed class Holder
    {
        public Box? Box { get; set; }
        public (Box, int) Pair { get; set; }
    }
}
