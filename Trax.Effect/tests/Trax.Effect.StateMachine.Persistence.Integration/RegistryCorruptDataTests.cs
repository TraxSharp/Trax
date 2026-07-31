using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Corrupt stored data must degrade to a typed problem, never a crash. We store an invalid snapshot
/// directly through the store (which does not validate), then drive the generic mutations, exercising the
/// load-error / invalid arms the happy paths can't reach.
/// </summary>
public class RegistryCorruptDataTests
{
    private static readonly ISnapshotPrincipal User = new FakePrincipal("u");

    private static ISnapshotMachineRegistry Registry()
    {
        var context = TestDb.NewContext();
        var provider = new ServiceCollection()
            .AddSingleton<IOrderCharge>(new CountingEffect())
            .BuildServiceProvider();
        return new SnapshotMachineRegistry(
            new IMachine[] { new TurnstileMachine(), new OrderMachine() },
            new EfSnapshotStore(context),
            new EfEffectClaimStore(context),
            new IdempotentEffect(new EfEffectClaimStore(context)),
            provider
        );
    }

    // A Locked turnstile carrying paidWith is shape-invalid (Locked forbids it), but the store persists it
    // as-is (validation lives one layer up), so a later read hits the rehydrate error path.
    private static Task StoreInvalid(Guid id) =>
        TestDb
            .NewStore()
            .Upsert(
                "u",
                id,
                new Snapshot
                {
                    Machine = "turnstile",
                    Version = 1,
                    State = "Locked",
                    Context = new JsonObject { ["paidWith"] = "quarter" },
                }
            );

    [Test]
    public async Task Advance_over_corrupt_stored_data_is_a_typed_problem()
    {
        var id = Guid.NewGuid();
        await StoreInvalid(id);

        var output = await new AdvanceSnapshotJunction(Registry(), User).Run(
            new AdvanceSnapshotInput
            {
                Machine = "turnstile",
                Id = id,
                Trigger = "Coin",
            }
        );

        output.Problem!.Code.Should().Be("invalid-context");
    }

    [Test]
    public async Task Load_over_corrupt_stored_data_is_a_typed_problem()
    {
        var id = Guid.NewGuid();
        await StoreInvalid(id);

        var output = await new LoadSnapshotJunction(Registry(), User).Run(
            new LoadSnapshotInput { Machine = "turnstile", Id = id }
        );

        output.Problem!.Code.Should().Be("invalid-context");
    }
}
