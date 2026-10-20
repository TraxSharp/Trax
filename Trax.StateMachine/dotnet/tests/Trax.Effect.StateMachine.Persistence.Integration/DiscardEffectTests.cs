using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// An instance an effect is running on is becoming the record of what the server did, so <c>discard</c> keeps it
/// and answers <c>DISCARD_NOT_ALLOWED</c>, whatever state it is in while the effect runs.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class DiscardEffectTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public async Task Discard_is_refused_while_an_effect_on_the_instance_is_running()
    {
        var charge = new HeldCharge();
        using var host = InstanceHost.Create(
            provider,
            services: s => s.AddSingleton<IOrderCharge>(charge)
        );
        var owner = $"u-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        await host.Seed(
            owner,
            id,
            new Snapshot
            {
                Machine = "ship",
                Version = 1,
                State = nameof(ShipState.Packing),
                Context = new JsonObject(),
            }
        );

        var send = Task.Run(async () =>
        {
            using var scope = host.Scope();
            return await host.Service(scope, "ship").Send(new Actor.Client(owner), id, "s-1");
        });
        await charge.Entered;

        DiscardOutcome discard;
        using (var scope = host.Scope())
            discard = await host.Service(scope, "ship").Discard(new Actor.Client(owner), id, "d-1");
        charge.Release();

        (await send).Should().BeOfType<AdvanceOutcome.Advanced>();
        discard
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(
                RefusalCode.DiscardNotAllowed,
                $"an instance an effect is running on is becoming the record of what it did. See {Adr}"
            );
        (await host.Rows(id))
            .Should()
            .ContainSingle("the instance stays")
            .Which.State.Should()
            .Be(nameof(ShipState.Shipped));
        charge.Calls.Should().Be(1);
    }

    /// <summary>A charge that holds its first run until released, so a test can act while the effect is running.</summary>
    private sealed class HeldCharge : IOrderCharge
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        public async Task<string> Run(
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        )
        {
            var n = Interlocked.Increment(ref _calls);
            _entered.TrySetResult();
            await _released.Task;
            return $"receipt-{n}";
        }
    }
}
