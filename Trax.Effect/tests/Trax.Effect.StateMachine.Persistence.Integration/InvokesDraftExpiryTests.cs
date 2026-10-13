using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Draft expiry never strands a run: system rows are exempt from the draft time-to-live, the sweep leaves a draft
/// that holds a live invoke token for its run's outcome, and a discard that cannot cancel a draft's run keeps the
/// draft. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokesDraftExpiryTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private static string UnlockedJson =>
        """{"machine":"turnstile","version":1,"state":"Unlocked","context":{"paidWith":"quarter"}}""";

    private static DateTimeOffset LongAgo => DateTimeOffset.UtcNow.AddHours(-1);

    private static DraftSweeper Sweeper(InstanceHost host) =>
        host.Services.GetRequiredService<DraftSweeper>();

    [Test]
    public async Task Draft_expiry_leaves_system_rows_alone()
    {
        using var host = InstanceHost.Create(provider, draftTtl: Ttl);
        var instance = await host.Start<SystemTurnstileMachine>(
            MachineKey.Of("expiry", Guid.NewGuid().ToString())
        );

        // A user's idle draft under the same id, and the idle system row beside it.
        using (var scope = host.Scope())
            await host.Service(scope, "turnstile").Autosave("u1", instance.Id, UnlockedJson);
        await host.Backdate(instance.Id, SnapshotOwnerKind.User, LongAgo);
        await host.Backdate(instance.Id, SnapshotOwnerKind.System, LongAgo);

        await Sweeper(host).SweepOnce();

        (await host.Rows(instance.Id))
            .Should()
            .ContainSingle($"the user's idle draft expires and the system row does not. See {Adr}")
            .Which.OwnerKind.Should()
            .Be(SnapshotOwnerKind.System);
    }

    [Test]
    [LeavesStuckRuns("seeds a draft holding a token with no run behind it, which the sweep keeps")]
    public async Task The_sweep_keeps_a_draft_holding_a_live_token_and_cancels_nothing()
    {
        var cancellation = new RecordingCancellation();
        using var host = InstanceHost.Create(provider, Ttl, cancellation);
        var id = Guid.NewGuid();
        var token = $"run-{Guid.NewGuid():N}";
        await SeedIdleDraftHolding(host, id, token);

        await Sweeper(host).SweepOnce();

        cancellation.Cancelled.Should().BeEmpty();
        (await host.Rows(id))
            .Should()
            .ContainSingle($"a draft waiting on its run is left for the run's outcome. See {Adr}")
            .Which.InvokeToken.Should()
            .Be(token);
    }

    [Test]
    [LeavesStuckRuns(
        "seeds a draft holding a token with no run behind it, and its failed cancel leaves the draft holding it"
    )]
    public async Task A_discard_whose_cancel_fails_keeps_the_draft()
    {
        var cancellation = new RecordingCancellation { Fail = true };
        using var host = InstanceHost.Create(provider, Ttl, cancellation);
        var id = Guid.NewGuid();
        await SeedIdleDraftHolding(host, id, $"run-{Guid.NewGuid():N}");

        using (var scope = host.Scope())
        {
            var discard = () =>
                host.Service(scope, "turnstile").Discard(new Actor.Client("u1"), id, "d1");
            await discard.Should().ThrowAsync<InvalidOperationException>();
        }

        (await host.Rows(id))
            .Should()
            .ContainSingle("a draft whose run could not be cancelled is not deleted");
    }

    [Test]
    public async Task An_idle_draft_with_no_token_cancels_nothing()
    {
        var cancellation = new RecordingCancellation();
        using var host = InstanceHost.Create(provider, Ttl, cancellation);
        var id = Guid.NewGuid();
        using (var scope = host.Scope())
            await host.Service(scope, "turnstile").Autosave("u1", id, UnlockedJson);
        await host.Backdate(id, SnapshotOwnerKind.User, LongAgo);

        await Sweeper(host).SweepOnce();

        cancellation.Cancelled.Should().BeEmpty();
        (await host.Rows(id)).Should().BeEmpty();
    }

    private static async Task SeedIdleDraftHolding(InstanceHost host, Guid id, string token)
    {
        using (var scope = host.Scope())
        {
            await host.Service(scope, "turnstile").Autosave("u1", id, UnlockedJson);
            var store = host.Instances(scope);
            var stored = await store.Get(DraftOwner.User("u1"), "turnstile", id);
            (
                await store.SetInvokeToken(
                    DraftOwner.User("u1"),
                    "turnstile",
                    id,
                    stored!.Token,
                    new InvokeTokenWrite(token)
                )
            )
                .Should()
                .BeTrue();
        }
        await host.Backdate(id, SnapshotOwnerKind.User, LongAgo);

        using var read = host.Scope();
        (
            await read
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.AsNoTracking()
                .SingleAsync(x => x.Id == id)
        )
            .InvokeToken.Should()
            .Be(token);
    }

    /// <summary>Records each cancel, or fails it.</summary>
    private sealed class RecordingCancellation : IInvokedRunCancellation
    {
        public List<string> Cancelled { get; } = [];
        public bool Fail { get; init; }

        public Task Cancel(string invokeToken, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException("The cancel could not be recorded.");
            Cancelled.Add(invokeToken);
            return Task.CompletedTask;
        }
    }
}
