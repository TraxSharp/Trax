using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.StateMachine.E2E.Fixtures;

namespace Trax.Samples.StateMachine.E2E.Tests;

/// <summary>
/// <c>checkout</c> is version 2: it added <c>total</c>, which every state requires. A draft written at
/// version 1 has none, and <c>MigrateFrom(1, ...)</c> backfills it from the item count whenever the
/// server reads one, so the old draft loads, advances and pays as a version 2 draft.
/// </summary>
[TestFixture]
public class ForwardMigrationTests : StateMachineTestFixture
{
    // TraxCallerSnapshotPrincipal keys a draft by the caller's principal id, which the API key scheme
    // qualifies with its name.
    private const string AliceUserKey = "TraxApiKey:alice";

    [Test]
    public async Task AVersion1DraftAnOlderHostStored_LoadsAsVersion2_WithTheTotalBackfilled()
    {
        var book = UniqueItem("book");
        var id = Guid.NewGuid();
        await StoreVersion1Draft(id, "Review", book, "pen");

        var loaded = await Machines.LoadAsync(Alice, Checkout, id);

        loaded.Snapshot["version"]!.GetValue<int>().Should().Be(2);
        loaded.State.Should().Be("Review");
        loaded.Context["items"]!.AsArray().Should().HaveCount(2);
        loaded.Context["total"]!.GetValue<long>().Should().Be(1998);
    }

    [Test]
    public async Task AVersion1Draft_PaysTheBackfilledTotal()
    {
        var book = UniqueItem("book");
        var id = Guid.NewGuid();
        await StoreVersion1Draft(id, "Review", book);

        var paid = await Machines.SendAsync(Alice, Checkout, id);

        paid.State.Should().Be("Paid");
        paid.Snapshot["version"]!.GetValue<int>().Should().Be(2);
        Charges.For(book).Should().ContainSingle().Which.AmountCents.Should().Be(999);
    }

    [Test]
    public async Task AVersion1SnapshotAnOlderClientSaves_IsStoredAsVersion2()
    {
        var book = UniqueItem("book");
        var id = Guid.NewGuid();
        var v1 = Wire(
            Checkout,
            1,
            "Cart",
            new JsonObject { ["items"] = new JsonArray(book), ["receipt"] = null }
        );

        var saved = await Machines.SaveAsync(Alice, Checkout, id, v1);

        saved.Snapshot["version"]!.GetValue<int>().Should().Be(2);
        saved.Context["total"]!.GetValue<long>().Should().Be(999);
        (await Machines.LoadAsync(Alice, Checkout, id)).Wire.Should().Be(saved.Wire);
    }

    [Test]
    public async Task AVersion2SnapshotWithNoTotal_IsInvalidContext()
    {
        // What makes the migration necessary: under version 2 a context without total is invalid.
        var id = Guid.NewGuid();
        var noTotal = Wire(
            Checkout,
            2,
            "Cart",
            new JsonObject { ["items"] = new JsonArray(), ["receipt"] = null }
        );

        (await Machines.SaveAsync(Alice, Checkout, id, noTotal))
            .ProblemCode.Should()
            .Be("invalid-context");
    }

    [Test]
    public async Task AVersionNewerThanTheServers_IsVersionMismatch()
    {
        var id = Guid.NewGuid();
        var v3 = Wire(
            Checkout,
            3,
            "Cart",
            new JsonObject
            {
                ["items"] = new JsonArray(),
                ["receipt"] = null,
                ["total"] = 0,
            }
        );

        (await Machines.SaveAsync(Alice, Checkout, id, v3))
            .ProblemCode.Should()
            .Be("version-mismatch");
    }

    /// <summary>
    /// Writes a version 1 draft for Alice straight through the store, the way the version 1 host left
    /// it in <c>snapshot_draft</c>. The store does not validate, so the row keeps its old shape until
    /// the server reads it.
    /// </summary>
    private static async Task StoreVersion1Draft(Guid id, string state, params string[] items)
    {
        using var scope = SharedStateMachineSetup.Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();

        var inserted = await store.Insert(
            AliceUserKey,
            id,
            new Snapshot
            {
                Machine = Checkout,
                Version = 1,
                State = state,
                Context = new JsonObject
                {
                    ["items"] = new JsonArray(items.Select(i => (JsonNode?)i).ToArray()),
                    ["receipt"] = null,
                },
            }
        );

        inserted.Should().BeTrue();
    }
}
