using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using SnapshotTransition = Trax.Effect.Models.SnapshotTransition.SnapshotTransition;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// The InMemory provider has no transaction and no foreign key, so the store writes the history and removes it with
/// the instance itself. Enforces
/// <c>Trax.StateMachine/docs/adr/0003-a-machine-instance-history-is-written-with-its-snapshot.md</c>.
/// </summary>
[Property(
    "adr",
    "Trax.StateMachine/docs/adr/0003-a-machine-instance-history-is-written-with-its-snapshot.md"
)]
public class TransitionHistoryInMemoryTests
{
    private const string Adr =
        "Trax.StateMachine/docs/adr/0003-a-machine-instance-history-is-written-with-its-snapshot.md";

    private const string User = "history-user";

    [Test]
    public async Task The_InMemory_provider_records_and_removes_the_history_too()
    {
        var quarter = new JsonObject { ["coin"] = "quarter" };
        var services = new ServiceCollection();
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddStateMachines(typeof(OrderMachine).Assembly)
        );
        var host = services.BuildServiceProvider();
        await using var hostDisposal = host.ConfigureAwait(false);
        var id = Guid.NewGuid();

        async Task<List<SnapshotTransition>> Rows()
        {
            var readScope = host.CreateAsyncScope();
            await using var readScopeDisposal = readScope.ConfigureAwait(false);
            return await readScope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotTransitions.AsNoTracking()
                .OrderBy(t => t.Revision)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        var scope = host.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var turnstile = scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .Service("turnstile")!;
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Upsert(User, id, TestDb.Parse(TestTurnstile.InitialJson))
            )
                .Should()
                .BeTrue();
            (await turnstile.Advance(User, id, "Coin", quarter))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        }

        var rows = await Rows();
        rows.Select(t => (t.Revision, t.Kind, t.Trigger))
            .Should()
            .Equal(
                [
                    (1L, SnapshotTransitionKind.Start, null),
                    (2L, SnapshotTransitionKind.Command, "Coin"),
                ],
                $"InMemory writes the same history. See {Adr}"
            );

        var discardScope = host.CreateAsyncScope();
        await using (discardScope.ConfigureAwait(false))
            (
                await discardScope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .Service("turnstile")!
                    .Discard(new Actor.Client(User), id, "d1")
            )
                .Should()
                .BeOfType<DiscardOutcome.Discarded>();

        (await Rows())
            .Should()
            .BeEmpty("InMemory enforces no foreign key, so the store removes it");
    }
}
