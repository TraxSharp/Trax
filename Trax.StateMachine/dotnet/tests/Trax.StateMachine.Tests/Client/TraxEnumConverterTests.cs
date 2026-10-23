using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Trax.Client;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// A generated enum is read and written by its members' wire names, and a name a newer host added is read as unknown
/// instead of failing the answer it came in.
/// </summary>
public class TraxEnumConverterTests
{
    [JsonConverter(typeof(TraxEnumConverter<RunState>))]
    public enum RunState
    {
        [JsonStringEnumMemberName("RUNNING")]
        Running,

        [JsonStringEnumMemberName("LAST24_HOURS")]
        Last24Hours,
    }

    public sealed record Run(RunState State, RunState? Previous);

    [Test]
    public void A_member_is_read_and_written_by_its_wire_name()
    {
        var run = JsonSerializer.Deserialize<Run>(
            """{"state":"LAST24_HOURS","previous":"RUNNING"}""",
            TraxJson.Options
        )!;

        run.Should().Be(new Run(RunState.Last24Hours, RunState.Running));
        JsonSerializer
            .Serialize(run, TraxJson.Options)
            .Should()
            .Be("""{"state":"LAST24_HOURS","previous":"RUNNING"}""");
    }

    [Test]
    public void A_name_the_enum_lacks_is_read_as_unknown_and_written_back_as_sent()
    {
        var run = JsonSerializer.Deserialize<Run>(
            """{"state":"PAUSED","previous":"WAITING"}""",
            TraxJson.Options
        )!;

        TraxEnum.IsUnknown(run.State).Should().BeTrue();
        TraxEnum.IsUnknown(run.Previous!.Value).Should().BeTrue();
        run.State.Should().NotBe(run.Previous.Value);
        TraxEnum.IsUnknown(RunState.Running).Should().BeFalse();
        JsonSerializer
            .Serialize(run, TraxJson.Options)
            .Should()
            .Be("""{"state":"PAUSED","previous":"WAITING"}""");
        JsonSerializer.Deserialize<RunState>("\"PAUSED\"", TraxJson.Options).Should().Be(run.State);
    }

    [Test]
    public void A_value_never_read_is_not_written()
    {
        var act = () => JsonSerializer.Serialize((RunState)42, TraxJson.Options);

        act.Should().Throw<JsonException>();
    }

    [Test]
    public void A_host_sending_names_without_end_is_refused()
    {
        var act = () =>
        {
            for (var i = 0; i <= TraxEnumConverter<Flood>.MaxUnknownNames; i++)
                JsonSerializer.Deserialize<Flood>($"\"NEW_{i}\"", TraxJson.Options);
        };

        act.Should().Throw<JsonException>().WithMessage("*more than 256*");
    }

    [JsonConverter(typeof(TraxEnumConverter<Flood>))]
    public enum Flood
    {
        One,
    }

    [Test]
    public void A_number_is_refused()
    {
        var act = () => JsonSerializer.Deserialize<RunState>("0", TraxJson.Options);

        act.Should().Throw<JsonException>();
    }
}
