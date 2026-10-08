using System.Text.Json;
using AwesomeAssertions;
using CsCheck;
using Trax.Core.Decisions;
using Trax.Core.Monad;
using static Trax.Core.Tests.Unit.UnitTests.Train.CheckpointFixtures;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// A state the chain accepts as a checkpoint reads back as the same value: written as JSON with
/// the serializer's defaults and read back, it hashes the same, so a decision asked after a
/// resume still replays.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[Property("adr", CheckpointFixtures.Adr)]
public class CheckpointStatePropertyTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private static readonly Gen<Checked> AChecked = Gen.Select(
        Gen.String,
        Gen.String,
        Gen.Int,
        (topic, source, pages) => new Checked(topic, source, pages)
    );

    private static readonly Gen<Rich> ARich = Gen.Select(
        Gen.String,
        Gen.Int,
        Gen.Enum<Source>(),
        Gen.String.List[0, 5],
        Gen.Dictionary(Gen.String, Gen.Int)[0, 5],
        AChecked.Null(),
        (name, count, lane, tags, scores, nested) =>
            new Rich(name, count, lane, tags, scores, nested)
    );

    [Test]
    public void StateDigest_of_a_generated_state_survives_a_round_trip()
    {
        CheckpointState.Problems(typeof(Rich)).Should().BeEmpty("the generated type is accepted");

        ARich.Sample(
            state =>
            {
                var read = JsonSerializer.Deserialize<Rich>(JsonSerializer.Serialize(state))!;

                StateDigest
                    .Of(read)
                    .Should()
                    .Be(
                        StateDigest.Of(state),
                        $"an accepted state reads back as the same value ({Adr})"
                    );
            },
            iter: 1000
        );
    }

    [Test]
    public void A_state_the_check_refuses_would_not_survive_the_round_trip()
    {
        // The reason for the refusal, shown: a field is not written, so it reads back empty.
        var state = new CheckpointDeclarationTests.WithField { Count = 3, Name = "a" };
        var read = JsonSerializer.Deserialize<CheckpointDeclarationTests.WithField>(
            JsonSerializer.Serialize(state)
        )!;

        CheckpointState
            .Problems(typeof(CheckpointDeclarationTests.WithField))
            .Should()
            .NotBeEmpty();
        StateDigest.Of(read).Should().NotBe(StateDigest.Of(state), Adr);
    }
}
