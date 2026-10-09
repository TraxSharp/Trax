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
/// Draft expiry never strands a run: system rows are exempt from the draft time-to-live, and a draft that holds a
/// live invoke token has its run cancelled before it is deleted. See
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

        using (var scope = host.Scope())
            (await host.Service(scope, "turnstile").Load("u1", instance.Id))
                .Should()
                .BeOfType<LoadResult.NotFound>();

        (await host.Rows(instance.Id))
            .Should()
            .ContainSingle($"the user's idle draft expires and the system row does not. See {Adr}")
            .Which.OwnerKind.Should()
            .Be(SnapshotOwnerKind.System);

        // A user with no draft under the id expires nothing either.
        using (var scope = host.Scope())
            (await host.Service(scope, "turnstile").Load("u2", instance.Id))
                .Should()
                .BeOfType<LoadResult.NotFound>();
        (await host.Rows(instance.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task Deleting_a_draft_with_a_live_token_cancels_its_run_first()
    {
        var cancellation = new RecordingCancellation();
        using var host = InstanceHost.Create(provider, Ttl, cancellation);
        var id = Guid.NewGuid();
        var token = $"run-{Guid.NewGuid():N}";

        await SeedIdleDraftHolding(host, id, token);
        cancellation.RowExists = () => host.Rows(id).Result.Count > 0;

        using (var scope = host.Scope())
            (await host.Service(scope, "turnstile").Load("u1", id))
                .Should()
                .BeOfType<LoadResult.NotFound>();

        cancellation
            .Cancelled.Should()
            .Equal([token], $"the live run is cancelled when its draft expires. See {Adr}");
        cancellation
            .RowExistedWhenCancelled.Should()
            .Equal([true], "the run is cancelled before its draft is deleted");
        (await host.Rows(id)).Should().BeEmpty();
    }

    [Test]
    [LeavesStuckRuns(
        "seeds a draft holding a token with no run behind it, and its failed cancel leaves the draft holding it"
    )]
    public async Task A_cancel_that_fails_keeps_the_draft()
    {
        var cancellation = new RecordingCancellation { Fail = true };
        using var host = InstanceHost.Create(provider, Ttl, cancellation);
        var id = Guid.NewGuid();
        await SeedIdleDraftHolding(host, id, $"run-{Guid.NewGuid():N}");

        using (var scope = host.Scope())
        {
            var load = () => host.Service(scope, "turnstile").Load("u1", id);
            await load.Should().ThrowAsync<InvalidOperationException>();
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

        using (var scope = host.Scope())
            await host.Service(scope, "turnstile").Load("u1", id);

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

    /// <summary>Records each cancel, and whether the draft still existed when it was asked for.</summary>
    private sealed class RecordingCancellation : IInvokedRunCancellation
    {
        public List<string> Cancelled { get; } = [];
        public List<bool> RowExistedWhenCancelled { get; } = [];
        public Func<bool>? RowExists { get; set; }
        public bool Fail { get; init; }

        public Task Cancel(string invokeToken, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException("The cancel could not be recorded.");
            Cancelled.Add(invokeToken);
            RowExistedWhenCancelled.Add(RowExists?.Invoke() ?? false);
            return Task.CompletedTask;
        }
    }
}
