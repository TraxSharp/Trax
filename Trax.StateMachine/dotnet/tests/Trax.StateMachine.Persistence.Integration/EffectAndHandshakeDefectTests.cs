using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Testing;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;
using Trax.Testing;

namespace Trax.StateMachine.Persistence.Integration;

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
            return Task.FromResult(receipt ?? Null.Of<string>());
        };

        // Completing with no receipt would leave the row looking claimed and in flight, and the next caller
        // after the lease would quietly deliver again. The run is reported as failed instead.
        var first = async () =>
            await new IdempotentEffect(TestDb.NewClaims())
                .RunOnce(key, effect, TimeSpan.Zero)
                .ConfigureAwait(false);
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
        await TestDb.Seed("u", id, OrderMachine.ReviewSnapshot(1));

        var send = await new SendSnapshotJunction(registry, user).Run(
            new SendSnapshotInput { Machine = "order", Id = id }
        );

        send.Problem.NotNull().Code.Should().Be(RefusalCode.DeliveryFailed);
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
            Task.FromResult(Null.Of<string>());
    }

    [Test]
    public void SchemaHash_must_not_read_null_while_another_request_is_computing_it()
    {
        var machine = SlowToBuildMachine.Blocking();

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
