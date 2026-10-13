using AwesomeAssertions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.Tests;

/// <summary>
/// A <c>machineChanged</c> stream ends <c>client-outdated</c> only when the row was written by a version the node
/// knows to be newer. Without an order of the machine's versions, a different IR hash says nothing about which is
/// newer, so the stream is kept rather than ended on an older node's write.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class MachineChangedOrderTests
{
    private static readonly PartsMachine Machine = new();

    private static SnapshotRowChange Changed(string irHash) =>
        new(
            SnapshotRowChangeKind.Changed,
            RowId: 7,
            Revision: 2,
            Machine: PartsMachine.MachineId,
            Id: Guid.NewGuid(),
            OwnerKind: Trax.Effect.Enums.SnapshotOwnerKind.User,
            UserKey: "user",
            Version: ((IMachineInternals)Machine).Version,
            State: nameof(PartsState.Picking),
            Context: "{}",
            IrHash: irHash,
            UpdatedAt: DateTimeOffset.UtcNow
        );

    [Test]
    public void Without_an_ir_order_another_hash_keeps_the_stream()
    {
        var other = new string('f', 64);

        var presented = MachineChangedSubscription.Present(
            Changed(other),
            Machine,
            (IMachineInternals)Machine,
            order: null
        );

        presented.Should().NotBeNull();
        presented!
            .Value.Ends.Should()
            .BeFalse(
                "with no order of the versions, an older node's write must not end the stream"
            );
    }

    [Test]
    public void An_order_that_says_newer_ends_the_stream_client_outdated()
    {
        var presented = MachineChangedSubscription.Present(
            Changed(new string('f', 64)),
            Machine,
            (IMachineInternals)Machine,
            new FixedOrder(true)
        );

        presented!.Value.Ends.Should().BeTrue();
        presented.Value.Event.Problem!.Code.Should().Be(DraftRefusalCodes.ClientOutdated);
    }

    private sealed class FixedOrder(bool? newer) : IMachineIrOrder
    {
        public bool? IsNewer(string machine, string irHash) => newer;
    }
}
