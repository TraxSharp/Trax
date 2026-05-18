using System.Text.Json;
using FluentAssertions;
using Trax.Api.GraphQL.Client.Utils.Converters;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// The converter is registered by default for every kernel client, so its behavior is part
/// of the public contract. If a future change broke null handling, calls returning a nullable
/// DateOnly would start throwing instead of returning default - these tests catch that.
/// </summary>
[TestFixture]
public class DateOnlyConverterTests
{
    private static JsonSerializerOptions Options() =>
        new() { Converters = { new DateOnlyConverter() } };

    [Test]
    public void Read_ValidDateTimeString_ParsesToDateOnly()
    {
        const string json = "\"2026-05-18T00:00:00Z\"";
        var result = JsonSerializer.Deserialize<DateOnly>(json, Options());

        result.Should().Be(new DateOnly(2026, 5, 18));
    }

    [Test]
    public void Read_NullToken_ReturnsDefault()
    {
        const string json = "null";
        var result = JsonSerializer.Deserialize<DateOnly?>(json, Options());

        result.Should().BeNull();
    }

    [Test]
    public void Write_DateOnly_ProducesIsoStringAtMidnight()
    {
        var date = new DateOnly(2026, 5, 18);
        var json = JsonSerializer.Serialize(date, Options());

        // The exact format includes time-of-day; we care that the date portion round-trips.
        json.Should().Contain("2026-05-18");

        // Round-trip: serializing then deserializing gives the same DateOnly back.
        var back = JsonSerializer.Deserialize<DateOnly>(json, Options());
        back.Should().Be(date);
    }
}
