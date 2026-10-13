using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <c>discard(id)</c> deletes the caller's own draft: someone else's id is answered exactly as a missing one, a
/// system-owned instance is refused, a repeat is harmless, and a draft holding a live invoked run has the run
/// cancelled before the draft goes.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class DiscardTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static string UnlockedJson =>
        """{"machine":"turnstile","version":1,"state":"Unlocked","context":{"paidWith":"quarter"}}""";

    private static string NewUser() => $"u-{Guid.NewGuid():N}";

    private static async Task<Guid> Draft(InstanceHost host, string user)
    {
        var id = Guid.NewGuid();
        using var scope = host.Scope();
        (await host.Service(scope, "turnstile").Autosave(user, id, UnlockedJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return id;
    }

    private static async Task<DiscardOutcome> Discard(
        InstanceHost host,
        string user,
        Guid id,
        string requestId = "d1"
    )
    {
        using var scope = host.Scope();
        return await host.Service(scope, "turnstile")
            .Discard(new Actor.Client(user), id, requestId);
    }

    [Test]
    public async Task Discard_of_another_users_id_answers_as_a_missing_one()
    {
        using var host = InstanceHost.Create(provider);
        var owner = NewUser();
        var id = await Draft(host, owner);
        var stranger = NewUser();

        var theirs = await Discard(host, stranger, id);
        var missing = await Discard(host, stranger, Guid.NewGuid());

        theirs
            .Should()
            .Be(
                missing,
                $"another user's id must not be told apart from one that does not exist. See {Adr}"
            );
        (await host.Rows(id)).Should().ContainSingle("the owner's draft is untouched");
    }

    [Test]
    public async Task Discard_of_a_system_instance_is_refused()
    {
        using var host = InstanceHost.Create(provider);
        var instance = await host.Start<SystemTurnstileMachine>(
            MachineKey.Of("discard", Guid.NewGuid().ToString())
        );

        DiscardOutcome outcome;
        using (var scope = host.Scope())
            outcome = await new SystemTurnstileMachine()
                .CreateService(
                    scope.ServiceProvider.GetRequiredService<ISnapshotStore>(),
                    scope.ServiceProvider.GetRequiredService<IEffectClaimStore>()
                )
                .Discard(new Actor.Client(NewUser()), instance.Id, "d1");

        outcome
            .Should()
            .BeOfType<DiscardOutcome.Rejected>(
                $"only code ends a system-owned instance. See {Adr}"
            );
        (await host.Rows(instance.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task Discard_is_idempotent()
    {
        using var host = InstanceHost.Create(provider);
        var user = NewUser();
        var id = await Draft(host, user);

        (await Discard(host, user, id, "d1")).Should().BeOfType<DiscardOutcome.Discarded>();
        (await Discard(host, user, id, "d1"))
            .Should()
            .BeOfType<DiscardOutcome.Discarded>(
                $"a retried discard finds the draft gone. See {Adr}"
            );
        (await Discard(host, user, id, "d2")).Should().BeOfType<DiscardOutcome.Discarded>();
        (await host.Rows(id)).Should().BeEmpty();
    }

    [Test]
    public async Task Discard_of_a_draft_holding_a_live_token_cancels_its_run_first()
    {
        var cancellation = new RecordingCancellation();
        using var host = InstanceHost.Create(provider, runCancellation: cancellation);
        var user = NewUser();
        var id = await Draft(host, user);
        var token = $"run-{Guid.NewGuid():N}";
        using (var scope = host.Scope())
        {
            var store = host.Instances(scope);
            var stored = await store.Get(DraftOwner.User(user), "turnstile", id);
            (
                await store.SetInvokeToken(
                    DraftOwner.User(user),
                    "turnstile",
                    id,
                    stored!.Token,
                    new InvokeTokenWrite(token)
                )
            )
                .Should()
                .BeTrue();
        }
        cancellation.RowExists = () => host.Rows(id).Result.Count > 0;

        try
        {
            (await Discard(host, user, id)).Should().BeOfType<DiscardOutcome.Discarded>();

            cancellation
                .Cancelled.Should()
                .Equal(
                    [token],
                    $"a discarded draft's live run is cancelled, never stranded. See {Adr}"
                );
            cancellation
                .RowExistedWhenCancelled.Should()
                .Equal([true], "the run is cancelled before its draft is deleted");
            (await host.Rows(id)).Should().BeEmpty();
        }
        finally
        {
            // The token has no run behind it: never leave it for the invariant check after the test.
            using var scope = host.Scope();
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.Where(x => x.Id == id)
                .ExecuteDeleteAsync();
        }
    }

    /// <summary>Records each cancel, and whether the draft still existed when it was asked for.</summary>
    private sealed class RecordingCancellation : IInvokedRunCancellation
    {
        public List<string> Cancelled { get; } = [];
        public List<bool> RowExistedWhenCancelled { get; } = [];
        public Func<bool>? RowExists { get; set; }

        public Task Cancel(string invokeToken, CancellationToken cancellationToken = default)
        {
            Cancelled.Add(invokeToken);
            RowExistedWhenCancelled.Add(RowExists?.Invoke() ?? false);
            return Task.CompletedTask;
        }
    }
}
