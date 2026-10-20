using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A system-owned instance's id is the UUIDv5 of its machine's fixed namespace and an injective encoding of its
/// key, so one key always names one instance and two different keys never share one. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
public class MachineInstanceIdTests
{
    [Test]
    public void Keys_a_b_c_split_differently_give_different_ids()
    {
        var left = MachineInstanceId.For("ingest", MachineKey.Of("a|b", "c"));
        var right = MachineInstanceId.For("ingest", MachineKey.Of("a", "b|c"));
        var joined = MachineInstanceId.For("ingest", MachineKey.Of("a|b|c"));

        new[] { left, right, joined }
            .Should()
            .OnlyHaveUniqueItems("the key's parts are length-prefixed, so no split collides");
    }

    [TestCase(new[] { "ab", "" }, new[] { "a", "b" })]
    [TestCase(new[] { "1:a" }, new[] { "a" })]
    [TestCase(new[] { "", "" }, new[] { "" })]
    [TestCase(new[] { "a:b", "c" }, new[] { "a", "b:c" })]
    public void Keys_that_differ_in_their_parts_give_different_ids(string[] one, string[] other)
    {
        MachineInstanceId
            .For("m", MachineKey.Of(one))
            .Should()
            .NotBe(MachineInstanceId.For("m", MachineKey.Of(other)));
    }

    [Test]
    public void One_key_gives_one_id_and_the_machine_is_part_of_it()
    {
        MachineInstanceId
            .For("ingest", MachineKey.Of("s", "p1"))
            .Should()
            .Be(MachineInstanceId.For("ingest", MachineKey.Of("s", "p1")));
        MachineInstanceId.For("ingest", "s").Should().NotBe(MachineInstanceId.For("export", "s"));
    }

    [Test]
    public void Ids_are_version_5_uuids_and_fixed_across_releases()
    {
        var id = MachineInstanceId.For("ingest", MachineKey.Of("source", "partition-1"));

        id.Version.Should().Be(5);
        (id.Variant & 0b1100).Should().Be(0b1000, "RFC 9562 variant");
        // Pinned: a change to the root namespace or the encoding orphans every stored instance.
        MachineInstanceId.Root.Should().Be(new Guid("0d3cd492-3485-4ca5-8fdc-e20b35ed5a85"));
        id.Should().Be(MachineInstanceId.For("ingest", MachineKey.Of("source", "partition-1")));
    }

    [Test]
    public void The_uuidv5_derivation_matches_the_rfc_example()
    {
        // RFC 9562 / RFC 4122: the DNS namespace and "www.example.com".
        MachineInstanceId
            .UuidV5(new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"), "www.example.com"u8)
            .Should()
            .Be(new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"));
    }

    [Test]
    public void A_key_refuses_no_parts_a_null_part_and_an_unpaired_surrogate()
    {
        ((Action)(() => MachineKey.Of())).Should().Throw<ArgumentException>();
        ((Action)(() => MachineKey.Of("a", null!))).Should().Throw<ArgumentException>();
        ((Action)(() => MachineKey.Of("\ud800"))).Should().Throw<ArgumentException>();
    }

    [Test]
    public void Keys_are_equal_by_their_parts_in_order()
    {
        MachineKey.Of("a", "b").Should().Be(MachineKey.Of("a", "b"));
        MachineKey.Of("a", "b").Should().NotBe(MachineKey.Of("b", "a"));
        ((MachineKey)"a").Should().Be(MachineKey.Of("a"));
    }
}
