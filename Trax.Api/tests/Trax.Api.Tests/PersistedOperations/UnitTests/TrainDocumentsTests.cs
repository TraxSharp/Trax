using AwesomeAssertions;
using Trax.Api.GraphQL.PersistedOperations.Services;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// A host generates no train document, but the prefix an earlier version stored them under stays reserved and its
/// ids still read back to a slot, so the grace-period sweep collects them and a request for one is answered as
/// outdated rather than run or refused as malformed.
/// <para>Enforces <c>Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md")]
public class TrainDocumentsTests
{
    private const string Adr =
        "Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md";

    private static readonly string Hash =
        new string('a', MachineDocuments.IrHashLength)
        + new string('b', MachineDocuments.TextHashLength);

    [TestCase("trax_train_mutation_a-b_c")]
    [TestCase("trax_train_query__findOrder")]
    [TestCase("trax_train_query_runs_findRun")]
    public void An_earlier_train_id_reads_back_to_its_slot(string slot) =>
        TrainDocuments
            .SlotOf(slot + "_" + Hash)
            .Should()
            .Be(
                slot,
                "an earlier version's id is still read, so it is collected and answered as outdated, per "
                    + Adr
            );

    [TestCase("trax_train_mutation__startRun")]
    [TestCase("trax_train_subscription__startRun_HASH")]
    [TestCase("trax_train_mutation___HASH")]
    [TestCase("trax_machine_order_get_HASH")]
    [TestCase("recovery_StartRun_v1")]
    public void An_id_that_is_not_a_train_document_names_no_slot(string id) =>
        TrainDocuments.SlotOf(id.Replace("HASH", Hash)).Should().BeNull();

    [Test]
    public void The_prefix_stays_reserved_and_never_shares_a_machines_slot()
    {
        var train = "trax_train_mutation__order_" + Hash;

        GeneratedDocuments.SlotOf(train, ["order"]).Should().BeNull();
        GeneratedDocuments
            .IsReserved(train)
            .Should()
            .BeTrue("the prefix stays reserved, per " + Adr);
        GeneratedDocuments.PrefixOf(train).Should().Be(TrainDocuments.ReservedIdPrefix);
    }
}
