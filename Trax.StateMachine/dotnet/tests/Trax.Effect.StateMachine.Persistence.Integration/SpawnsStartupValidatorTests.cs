using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The host refuses a spawning state whose child it cannot start or hear from: a child it does not register, one owned
/// otherwise than its parent, one that never ends, and one whose initial state waits on work. See
/// <c>Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md</c>.
/// </summary>
public class SpawnsStartupValidatorTests
{
    private const string Adr =
        "Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md";

    // Generic, so the assembly scan that registers the fakes for every other test passes them over.
    private sealed class ParentOf<TChild> : Machine<ConsignmentState, ConsignmentTrigger>
        where TChild : class
    {
        protected override void Configure(IMachineBuilder<ConsignmentState, ConsignmentTrigger> m)
        {
            m.Id("parent-of")
                .Version(1)
                .StartsAt(
                    ConsignmentState.Open,
                    () => new JsonObject { ["parcelId"] = null, ["label"] = "" }
                );
            m.In(ConsignmentState.Open)
                .Context<ConsignmentMachine.OpenContext>()
                .On(ConsignmentTrigger.Pack)
                .To(ConsignmentState.Packing);
            m.In(ConsignmentState.Packing)
                .Context<ConsignmentMachine.OpenContext>()
                .Spawns<TChild, ConsignmentMachine.OpenContext, ParcelResult>(c => c.ParcelId)
                .OnDone(ConsignmentState.Sent)
                .OnFailed(ConsignmentState.Lost)
                .OnCancelled(ConsignmentState.Recalled);
        }
    }

    private sealed class EndlessChild<T> : Machine<InspectionState, InspectionTrigger>
    {
        protected override void Configure(IMachineBuilder<InspectionState, InspectionTrigger> m)
        {
            m.Id("endless-child").Version(1).StartsAt(InspectionState.Open, () => new JsonObject());
            m.In(InspectionState.Open).On(InspectionTrigger.Pass).To(InspectionState.Passed);
        }
    }

    private sealed class BusyChild<T> : Machine<BuildState, BuildTrigger>
    {
        protected override void Configure(IMachineBuilder<BuildState, BuildTrigger> m)
        {
            m.Id("busy-child")
                .Version(1)
                .StartsAt(BuildState.Building, () => new JsonObject { ["source"] = "repo" });
            m.In(BuildState.Building)
                .Invokes<IBuildTrain, BuildInput, BuildOutput>(ctx => new BuildInput("repo"))
                .OnDone(BuildState.Built)
                .OnFailed(BuildState.BuildFailed)
                .OnCancelled(BuildState.BuildCancelled);
            m.In(BuildState.Built).Ends();
        }
    }

    private static IReadOnlyList<string> Problems(IMachine parent, params IMachine[] others)
    {
        using var host = InstanceHost.Create(StoreProvider.Sqlite);
        return SpawnsStartupValidator.Problems(parent, [parent, .. others], host.Services);
    }

    [Test]
    public void A_child_owned_as_its_parent_with_an_end_passes() =>
        Problems(new ParentOf<ParcelMachine>(), new ParcelMachine()).Should().BeEmpty();

    [Test]
    public void A_child_the_host_does_not_register_is_refused() =>
        Problems(new ParentOf<ParcelMachine>())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not register", $"the child could never be started. See {Adr}");

    [Test]
    public void A_system_child_of_a_user_parent_is_refused() =>
        Problems(new ParentOf<SystemParcelMachine>(), new SystemParcelMachine())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("a user-owned parent's child is its user's");

    [Test]
    public void A_child_that_never_ends_is_refused() =>
        Problems(new ParentOf<EndlessChild<int>>(), new EndlessChild<int>())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("declares no state it ends in");

    [Test]
    public void A_child_whose_initial_state_waits_on_work_is_refused() =>
        Problems(new ParentOf<BusyChild<int>>(), new BusyChild<int>())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("initial state Building invokes a train");
}
