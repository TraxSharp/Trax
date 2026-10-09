using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Testing;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public enum GateState
{
    A,
    B,
}

public enum GateTrigger
{
    Go,
}

/// <summary>A declarative machine whose first <see cref="Configure"/> blocks until released.</summary>
public sealed class SlowToBuildMachine : Machine<GateState, GateTrigger>
{
    public readonly ManualResetEventSlim Release = new(false);
    public readonly ManualResetEventSlim Entered = new(false);

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(10));
        m.Id("slow").Version(1).StartsAt(GateState.A, () => new JsonObject());
        m.In(GateState.A).Context().On(GateTrigger.Go).To(GateState.B);
        m.In(GateState.B).Context();
    }
}

public class EffectAndHandshakeDefectTests
{
    [TestCase(null)]
    [TestCase("")]
    [LeavesStuckRuns("ends by claiming the key to prove it is free, and keeps that claim")]
    public async Task An_effect_that_returns_no_receipt_fails_instead_of_being_recorded_in_flight(
        string? receipt
    )
    {
        var key = $"idem:{Guid.NewGuid()}";
        var calls = 0;
        Func<Task<string>> effect = () =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(receipt!);
        };

        // Completing with no receipt would leave the row looking claimed and in flight, and the next caller
        // after the lease would quietly deliver again. The run is reported as failed instead.
        var first = async () =>
            await new IdempotentEffect(TestDb.NewClaims()).RunOnce(key, effect, TimeSpan.Zero);
        await first
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{key}*receipt*");
        calls.Should().Be(1);

        // The claim is released rather than left in flight, so nothing is waiting on a lease: a retry is a
        // new, visible attempt (the caller was told the first failed), exactly as after an effect that threw.
        (await TestDb.NewClaims().GetReceipt(key))
            .Should()
            .BeNull();
        (await TestDb.NewClaims().TryClaim(key, TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Won>("the failed run left no claim behind");
    }

    [Test]
    public async Task A_send_whose_effect_returns_no_receipt_reports_delivery_failed_and_leaves_the_draft()
    {
        var id = Guid.NewGuid();
        var registry = new SnapshotMachineRegistry(
            new IMachine[] { new OrderMachine() },
            TestDb.NewStore(),
            TestDb.NewClaims(),
            new IdempotentEffect(TestDb.NewClaims()),
            new ServiceCollection()
                .AddSingleton<IOrderCharge>(new NoReceiptCharge())
                .BuildServiceProvider()
        );
        var user = new FakePrincipal("u");
        (
            await new SaveSnapshotJunction(registry, user).Run(
                new SaveSnapshotInput
                {
                    Machine = "order",
                    Id = id,
                    Snapshot = OrderMachine.ReviewSnapshot(1),
                }
            )
        ).Problem.Should().BeNull();

        var send = await new SendSnapshotJunction(registry, user).Run(
            new SendSnapshotInput { Machine = "order", Id = id }
        );

        send.Problem!.Code.Should().Be("delivery-failed");
        (
            await new LoadSnapshotJunction(registry, user).Run(
                new LoadSnapshotInput { Machine = "order", Id = id }
            )
        )
            .Snapshot.Should()
            .Contain("\"state\":\"Review\"");
    }

    private sealed class NoReceiptCharge : IOrderCharge
    {
        public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.FromResult<string>(null!);
    }

    [Test]
    public void SchemaHash_must_not_read_null_while_another_request_is_computing_it()
    {
        var machine = new SlowToBuildMachine();

        var first = Task.Run(() => machine.SchemaHash);
        machine.Entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        // A second request arrives while the first is still building the machine.
        var second = machine.SchemaHash;
        machine.Release.Set();

        first.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        first.Result.Should().NotBeNull();
        second.Should().Be(first.Result, "a null hash turns the schema-mismatch check off");
    }
}
