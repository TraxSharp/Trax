using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using SnapshotDraft = Trax.Effect.Models.SnapshotDraft.SnapshotDraft;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The storage the invoked-train slices build on: system rows keyed by machine and id, the <c>invoke_token</c>
/// column with its unique index, and the conditional update an outcome is applied with, which matches once. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokeTokenStoreTests(StoreProvider provider)
{
    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider);

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static Snapshot Turnstile(string state, JsonObject? context = null) =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = state,
            Context = context ?? new JsonObject(),
        };

    private static string NewToken() => $"run-{Guid.NewGuid():N}";

    private async Task<(Guid Id, Guid Token)> SystemRow(string state = "Locked")
    {
        using var scope = _host.Scope();
        var store = _host.Instances(scope);
        var id = Guid.NewGuid();
        (await store.Insert(DraftOwner.System, id, Turnstile(state))).Should().BeTrue();
        return (id, (await store.Get(DraftOwner.System, "turnstile", id))!.Token);
    }

    [Test]
    public async Task A_second_system_row_under_one_machine_and_id_is_refused_as_a_lost_race()
    {
        var (id, _) = await SystemRow();

        using var scope = _host.Scope();
        (await _host.Instances(scope).Insert(DraftOwner.System, id, Turnstile("Locked")))
            .Should()
            .BeFalse();
        (await _host.Rows(id)).Should().ContainSingle();
    }

    [Test]
    [LeavesStuckRuns(
        "writes invoke tokens straight into the store, with no queued run behind them, to test the column itself"
    )]
    public async Task The_token_unique_index_refuses_a_second_row_with_the_same_token()
    {
        var token = NewToken();
        var (first, firstToken) = await SystemRow();
        var (second, secondToken) = await SystemRow();

        using var scope = _host.Scope();
        var store = _host.Instances(scope);
        (
            await store.SetInvokeToken(
                DraftOwner.System,
                "turnstile",
                first,
                firstToken,
                new InvokeTokenWrite(token)
            )
        )
            .Should()
            .BeTrue();
        (
            await store.SetInvokeToken(
                DraftOwner.System,
                "turnstile",
                second,
                secondToken,
                new InvokeTokenWrite(token)
            )
        )
            .Should()
            .BeFalse("one token names one entry into one state");

        (await _host.Rows(second)).Single().InvokeToken.Should().BeNull();
    }

    [Test]
    [LeavesStuckRuns(
        "writes invoke tokens straight into the store, with no queued run behind them, to test the column itself"
    )]
    public async Task The_database_refuses_a_duplicate_token_written_directly()
    {
        var token = NewToken();
        var (first, firstToken) = await SystemRow();
        using (var scope = _host.Scope())
            await _host
                .Instances(scope)
                .SetInvokeToken(
                    DraftOwner.System,
                    "turnstile",
                    first,
                    firstToken,
                    new InvokeTokenWrite(token)
                );

        using var write = _host.Scope();
        var db = write.ServiceProvider.GetRequiredService<IDataContext>();
        db.SnapshotDrafts.Add(
            new SnapshotDraft
            {
                Id = Guid.NewGuid(),
                OwnerKind = SnapshotOwnerKind.System,
                Machine = "turnstile",
                Version = 1,
                State = "Locked",
                ConcurrencyToken = Guid.NewGuid(),
                UpdatedAt = DateTimeOffset.UtcNow,
                InvokeToken = token,
            }
        );
        var save = () => ((DbContext)db).SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task The_owner_check_refuses_a_system_row_with_a_user_key_and_a_user_row_without_one()
    {
        foreach (
            var (kind, key) in new[]
            {
                (SnapshotOwnerKind.System, (string?)"u1"),
                (SnapshotOwnerKind.User, null),
            }
        )
        {
            using var scope = _host.Scope();
            var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
            db.SnapshotDrafts.Add(
                new SnapshotDraft
                {
                    Id = Guid.NewGuid(),
                    OwnerKind = kind,
                    UserKey = key,
                    Machine = "turnstile",
                    Version = 1,
                    State = "Locked",
                    ConcurrencyToken = Guid.NewGuid(),
                    UpdatedAt = DateTimeOffset.UtcNow,
                }
            );
            var save = () => ((DbContext)db).SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>($"{kind} with key '{key}'");
        }
    }

    [Test]
    public async Task The_conditional_update_by_token_matches_once_and_then_not_again()
    {
        var token = NewToken();
        var (id, concurrency) = await SystemRow();
        using (var scope = _host.Scope())
            (
                await _host
                    .Instances(scope)
                    .SetInvokeToken(
                        DraftOwner.System,
                        "turnstile",
                        id,
                        concurrency,
                        new InvokeTokenWrite(token)
                    )
            )
                .Should()
                .BeTrue();

        var outcome = Turnstile("Unlocked", new JsonObject { ["paidWith"] = "quarter" });
        using (var scope = _host.Scope())
        {
            var store = _host.Instances(scope);
            (await store.ApplyByInvokeToken(token, outcome, nextInvokeToken: null))
                .Should()
                .BeTrue();
            (await store.ApplyByInvokeToken(token, outcome, nextInvokeToken: null))
                .Should()
                .BeFalse("a duplicate delivery applies once");
            (await store.GetByInvokeToken(token)).Should().BeNull();
        }

        var row = (await _host.Rows(id)).Single();
        row.State.Should().Be("Unlocked");
        row.InvokeToken.Should().BeNull();
    }

    [Test]
    [LeavesStuckRuns(
        "writes invoke tokens straight into the store, with no queued run behind them, to test the column itself"
    )]
    public async Task A_replaced_token_does_not_match_and_an_expected_concurrency_token_is_honoured()
    {
        var stale = NewToken();
        var live = NewToken();
        var (id, concurrency) = await SystemRow();

        using var scope = _host.Scope();
        var store = _host.Instances(scope);
        await store.SetInvokeToken(
            DraftOwner.System,
            "turnstile",
            id,
            concurrency,
            new InvokeTokenWrite(stale)
        );
        var read = await store.GetByInvokeToken(stale);
        read!.Owner.Should().Be(DraftOwner.System);
        read.Snapshot.InvokeToken.Should().Be(stale);
        (
            await store.SetInvokeToken(
                DraftOwner.System,
                "turnstile",
                id,
                read.Snapshot.Token,
                new InvokeTokenWrite(live)
            )
        )
            .Should()
            .BeTrue();

        (await store.ApplyByInvokeToken(stale, Turnstile("Locked"), null))
            .Should()
            .BeFalse("a completion whose token was replaced is a no-transition");
        (await store.ApplyByInvokeToken(live, Turnstile("Locked"), null, Guid.NewGuid()))
            .Should()
            .BeFalse("the row no longer carries that concurrency token");
        (await store.GetByInvokeToken(live)).Should().NotBeNull();
    }

    [Test]
    [LeavesStuckRuns(
        "writes invoke tokens straight into the store, with no queued run behind them, to test the column itself"
    )]
    public async Task A_users_update_never_writes_or_clears_the_invoke_token()
    {
        var id = Guid.NewGuid();
        var token = NewToken();
        using var scope = _host.Scope();
        var store = _host.Instances(scope);
        await store.Insert(DraftOwner.User("u1"), id, Turnstile("Locked"));
        var read = await store.Get(DraftOwner.User("u1"), "turnstile", id);
        await store.SetInvokeToken(
            DraftOwner.User("u1"),
            "turnstile",
            id,
            read!.Token,
            new InvokeTokenWrite(token)
        );

        var users = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
        var stored = await users.Get("u1", "turnstile", id);
        stored!.InvokeToken.Should().Be(token);
        (
            await users.UpdateWithRequest(
                "u1",
                id,
                Turnstile("Unlocked", new JsonObject { ["paidWith"] = "quarter" }),
                stored.Token,
                null
            )
        )
            .Should()
            .BeTrue();
        (await users.Upsert("u1", id, Turnstile("Locked"))).Should().BeTrue();

        (await _host.Rows(id)).Single().InvokeToken.Should().Be(token);
    }
}
