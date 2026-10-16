using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A state that spawns a child machine: entering it creates the child for the same owner, the child's end moves the
/// parent through the matching outcome, leaving the state removes the child, and the reconciler delivers what the
/// write that ended the child did not. See
/// <c>Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class SpawnedChildTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md";

    private const string User = "alice";

    [Test]
    public async Task Entering_the_state_spawns_a_child_the_same_user_owns()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);

        var parent = await Parent(host, id);
        parent.State.Should().Be(nameof(ConsignmentState.Packing));
        var childId = ChildId(parent);
        var child = (await host.Rows(childId)).Should().ContainSingle().Subject;
        child.Machine.Should().Be(ParcelMachine.Id);
        child.State.Should().Be(nameof(ParcelState.Packing));
        child.OwnerKind.Should().Be(SnapshotOwnerKind.User);
        child.UserKey.Should().Be(User, $"a user-owned parent's child is its user's. See {Adr}");
        child.SpawnedBy.Should().Be(parent.InvokeToken).And.NotBeNull();
    }

    [Test]
    public async Task The_childs_end_moves_the_parent_through_OnDone()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);
        var childId = ChildId(await Parent(host, id));

        using (var scope = host.Scope())
            (
                await host.Service(scope, ParcelMachine.Id)
                    .Advance(User, childId, "Seal", new JsonObject { ["label"] = "P-1" })
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        var parent = await Parent(host, id);
        parent.State.Should().Be(nameof(ConsignmentState.Sent));
        JsonNode.Parse(parent.Context)!["label"]!
            .GetValue<string>()
            .Should()
            .Be("P-1", "OnDone reduces the child's context into the parent's");
        parent.InvokeToken.Should().BeNull();
    }

    [Test]
    public async Task A_child_that_fails_moves_the_parent_through_OnFailed()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);
        var childId = ChildId(await Parent(host, id));

        using (var scope = host.Scope())
            await host.Service(scope, ParcelMachine.Id).Advance(User, childId, "Drop");

        (await Parent(host, id)).State.Should().Be(nameof(ConsignmentState.Lost));
    }

    [Test]
    public async Task Leaving_the_state_removes_the_child()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);
        var childId = ChildId(await Parent(host, id));

        using (var scope = host.Scope())
            await host.Service(scope, ConsignmentMachine.Id).Advance(User, id, "Abandon");

        (await Parent(host, id)).State.Should().Be(nameof(ConsignmentState.Abandoned));
        (await host.Rows(childId))
            .Should()
            .BeEmpty($"leaving the spawning state cancels its child. See {Adr}");
    }

    [Test]
    public async Task Leaving_the_state_removes_what_the_child_waits_on_too()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);
        var childId = ChildId(await Parent(host, id));
        using (var scope = host.Scope())
            await host.Service(scope, ParcelMachine.Id).Advance(User, childId, "Inspect");
        var child = (await host.Rows(childId)).Single();
        var grandchildId = Guid.Parse(
            JsonNode.Parse(child.Context)!["inspectionId"]!.GetValue<string>()
        );
        (await host.Rows(grandchildId)).Should().ContainSingle();

        using (var scope = host.Scope())
            await host.Service(scope, ConsignmentMachine.Id).Advance(User, id, "Abandon");

        (await host.Rows(childId)).Should().BeEmpty();
        (await host.Rows(grandchildId))
            .Should()
            .BeEmpty($"removing a child removes what it waits on. See {Adr}");
    }

    [Test]
    public async Task A_system_parent_hears_from_its_system_child()
    {
        using var host = InstanceHost.Create(provider);
        var key = MachineKey.Of("consignment", Guid.NewGuid().ToString("N"));
        var started = await host.Start<SystemConsignmentMachine>(key);
        using (var scope = host.Scope())
        {
            var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();
            (await instances.Advance<SystemConsignmentMachine>(key, "Pack"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        }
        var parent = (await host.Rows(started.Id)).Single();
        var childId = ChildId(parent);
        var child = (await host.Rows(childId)).Should().ContainSingle().Subject;
        child.OwnerKind.Should().Be(SnapshotOwnerKind.System);

        using (var scope = host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Advance<SystemParcelMachine>(
                        childId,
                        "Seal",
                        new JsonObject { ["label"] = "S-1" }
                    )
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        var moved = (await host.Rows(started.Id)).Single();
        moved.State.Should().Be(nameof(ConsignmentState.Sent));
        JsonNode.Parse(moved.Context)!["label"]!.GetValue<string>().Should().Be("S-1");
    }

    [Test]
    public async Task The_sweep_delivers_an_end_the_childs_write_did_not()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);
        var childId = ChildId(await Parent(host, id));

        // Moved by host code straight through the table, so nothing delivers it but the sweep.
        await host.Execute(
            $"UPDATE {host.DraftTable} SET state = 'Packed', context = @p0{(host.Provider == StoreProvider.Postgres ? "::jsonb" : "")} WHERE id = @p1",
            """{"label":"P-2"}""",
            Id(host, childId)
        );
        (await Parent(host, id)).State.Should().Be(nameof(ConsignmentState.Packing));

        await host.Services.GetRequiredService<InvokeOutcomeReconciler>().SweepOnce(default);

        var parent = await Parent(host, id);
        parent.State.Should().Be(nameof(ConsignmentState.Sent));
        JsonNode.Parse(parent.Context)!["label"]!.GetValue<string>().Should().Be("P-2");
    }

    [Test]
    public async Task A_child_that_disappears_moves_the_parent_through_OnCancelled()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);
        var childId = ChildId(await Parent(host, id));

        await host.Execute($"DELETE FROM {host.DraftTable} WHERE id = @p0", Id(host, childId));
        await host.Services.GetRequiredService<InvokeOutcomeReconciler>().SweepOnce(default);

        (await Parent(host, id)).State.Should().Be(nameof(ConsignmentState.Recalled));
    }

    [Test]
    public async Task A_waiting_parents_child_cannot_be_discarded_or_saved()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);
        var childId = ChildId(await Parent(host, id));

        using var scope = host.Scope();
        var parcel = host.Service(scope, ParcelMachine.Id);
        (await parcel.Discard(new Actor.Client(User), childId, "d1"))
            .Should()
            .BeOfType<DiscardOutcome.Rejected>(
                $"the parent waits on the child; only the parent's own transitions remove it. See {Adr}"
            );
        (
            await parcel.Autosave(
                User,
                childId,
                """{"machine":"parcel","version":1,"state":"Packed","context":{"label":"forged"}}"""
            )
        )
            .Should()
            .BeOfType<AutosaveResult.Rejected>(
                "a save would end the child without its declared events"
            );
        (await Parent(host, id)).State.Should().Be(nameof(ConsignmentState.Packing));
    }

    [Test]
    public async Task The_parent_refuses_its_outcome_as_an_event()
    {
        using var host = InstanceHost.Create(provider);
        var id = await Packing(host);

        using var scope = host.Scope();
        (
            await host.Service(scope, ConsignmentMachine.Id)
                .Advance(User, id, "Packing.done", new JsonObject { ["label"] = "forged" })
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>($"only the child's end delivers it. See {Adr}");
        (await Parent(host, id)).State.Should().Be(nameof(ConsignmentState.Packing));
    }

    // Alice's consignment, seeded in Open and packed, which spawns its parcel.
    private static async Task<Guid> Packing(InstanceHost host)
    {
        var id = Guid.NewGuid();
        await host.Seed(User, id, ConsignmentMachine.At(ConsignmentState.Open));
        using var scope = host.Scope();
        var packed = await host.Service(scope, ConsignmentMachine.Id).Advance(User, id, "Pack");
        packed.Should().BeOfType<AdvanceOutcome.Advanced>(packed.ToString());
        return id;
    }

    private static async Task<Trax.Effect.Models.SnapshotDraft.SnapshotDraft> Parent(
        InstanceHost host,
        Guid id
    ) => (await host.Rows(id)).Single(r => r.Machine == ConsignmentMachine.Id);

    private static Guid ChildId(Trax.Effect.Models.SnapshotDraft.SnapshotDraft parent) =>
        Guid.Parse(JsonNode.Parse(parent.Context)!["parcelId"]!.GetValue<string>());

    // SQLite stores a Guid as its uppercase text.
    private static object Id(InstanceHost host, Guid id) =>
        host.Provider == StoreProvider.Sqlite ? id.ToString().ToUpperInvariant() : id;
}
