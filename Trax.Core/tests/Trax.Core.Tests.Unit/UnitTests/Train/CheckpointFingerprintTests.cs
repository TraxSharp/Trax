using System.Text.Json.Serialization;
using AwesomeAssertions;
using Trax.Core.Monad;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// A checkpoint state's fingerprint changes whenever its stored JSON would read back as a
/// different value. Each pair below is one type before and after a change that keeps every
/// member's name and type: its fingerprint, read as if both were the same type, must differ.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[Property("adr", CheckpointFixtures.Adr)]
public class CheckpointFingerprintTests : TestSetup
{
    [Test]
    public void An_enum_member_whose_value_changes_fingerprints_differently() =>
        Differ(typeof(LaneBefore), typeof(LaneAfter), "the serializer writes an enum's number");

    [Test]
    public void An_enum_whose_underlying_type_changes_fingerprints_differently() =>
        Differ(typeof(LaneBefore), typeof(LaneWide), "a wider number reads back differently");

    [Test]
    public void A_member_given_a_converter_fingerprints_differently() =>
        Differ(typeof(PlainLane), typeof(StringLane), "a converter changes what is written");

    [Test]
    public void A_member_given_number_handling_fingerprints_differently() =>
        Differ(typeof(PlainCount), typeof(QuotedCount), "number handling changes what is written");

    [Test]
    public void A_type_given_a_converter_fingerprints_differently() =>
        Differ(typeof(PlainLane), typeof(ConvertedState), "a type's converter writes it");

    [Test]
    public void The_same_type_fingerprints_the_same() =>
        CheckpointState
            .Fingerprint(typeof(StringLane))
            .Should()
            .Be(CheckpointState.Fingerprint(typeof(StringLane)));

    /// <summary>
    /// Asserts the two types fingerprint differently once their own names are set aside, as one
    /// type would before and after the change.
    /// </summary>
    private static void Differ(Type before, Type after, string because)
    {
        var shape = CheckpointState.Shape(before).Replace(before.FullName!, "T");
        CheckpointState.Shape(after).Replace(after.FullName!, "T").Should().NotBe(shape, because);
    }

    public enum LaneBefore
    {
        Web = 0,
        Papers = 1,
    }

    public enum LaneAfter
    {
        Web = 0,
        Papers = 2,
    }

    public enum LaneWide : long
    {
        Web = 0,
        Papers = 1,
    }

    public sealed record PlainLane(LaneBefore Lane);

    public sealed record StringLane(
        [property: JsonConverter(typeof(JsonStringEnumConverter))] LaneBefore Lane
    );

    public sealed record PlainCount(int Count);

    public sealed record QuotedCount(
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] int Count
    );

    [JsonConverter(typeof(ConvertedStateConverter))]
    public sealed record ConvertedState(LaneBefore Lane);

    public sealed class ConvertedStateConverter : JsonConverter<ConvertedState>
    {
        public override ConvertedState Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options
        ) => new((LaneBefore)reader.GetInt32());

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            ConvertedState value,
            System.Text.Json.JsonSerializerOptions options
        ) => writer.WriteNumberValue((int)value.Lane);
    }
}
