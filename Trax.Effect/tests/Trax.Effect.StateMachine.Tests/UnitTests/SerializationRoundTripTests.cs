using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The serialized bytes are a contract: the envelope is in fixed order (machine, version, state,
/// context) and the context keys are canonicalized (RFC 8785, ordinal sort) so both runtimes emit
/// identical bytes regardless of how the object was built.
/// </summary>
public class SerializationRoundTripTests
{
    [Test]
    public void Serialize_Locked_pins_the_exact_wire_bytes()
    {
        var snapshot = new Snapshot
        {
            Machine = "turnstile",
            Version = 1,
            State = "Locked",
            Context = new JsonObject(),
        };

        TestTurnstile
            .Machine.Serialize(snapshot)
            .Should()
            .Be("{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}");
    }

    [Test]
    public void Serialize_Unlocked_pins_the_exact_wire_bytes()
    {
        var snapshot = new Snapshot
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = "quarter" },
        };

        TestTurnstile
            .Machine.Serialize(snapshot)
            .Should()
            .Be(
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}"
            );
    }

    [Test]
    public void Serialize_canonicalizes_context_keys_ordinally_regardless_of_insertion_order()
    {
        // Keys inserted out of order (and nested) must come out sorted by UTF-16 code unit, recursively.
        var snapshot = new Snapshot
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject
            {
                ["z"] = 1,
                ["a"] = 2,
                ["m"] = new JsonObject { ["y"] = true, ["b"] = false },
            },
        };

        TestTurnstile
            .Machine.Serialize(snapshot)
            .Should()
            .Be(
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"a\":2,\"m\":{\"b\":false,\"y\":true},\"z\":1}}"
            );
    }

    [Test]
    public void Serialize_keeps_array_order_but_sorts_object_keys_inside_arrays()
    {
        var snapshot = new Snapshot
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject
            {
                ["items"] = new JsonArray(
                    new JsonObject { ["b"] = 2, ["a"] = 1 },
                    new JsonObject { ["a"] = 3 }
                ),
            },
        };

        TestTurnstile
            .Machine.Serialize(snapshot)
            .Should()
            .Be(
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"items\":[{\"a\":1,\"b\":2},{\"a\":3}]}}"
            );
    }

    [Test]
    public void Serialize_then_Rehydrate_is_identity_for_a_valid_snapshot()
    {
        var snapshot = new Snapshot
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = "dollar" },
        };

        var round = TestTurnstile.Machine.Rehydrate(TestTurnstile.Machine.Serialize(snapshot));

        round.Should().BeOfType<RehydrationResult.Ok>().Which.Snapshot.Should().Be(snapshot);
    }
}
