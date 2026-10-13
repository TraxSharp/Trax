using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A draft belongs to one machine. Two machines may give one user a draft under the same id (a well-known id
/// per user is allowed), and neither may read, overwrite or delete the other's.
/// </summary>
public class DraftMachineScopeTests
{
    private static ISnapshotDraftService Turnstiles() =>
        new TurnstileMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());

    private static ISnapshotDraftService Notes() =>
        new NoteMachine().CreateService(TestDb.NewStore(), TestDb.NewClaims());

    private const string Note =
        "{\"machine\":\"note\",\"version\":1,\"state\":\"Draft\",\"context\":{\"text\":\"hi\"}}";

    [Test]
    public async Task Two_machines_keep_separate_drafts_under_one_id()
    {
        var id = Guid.NewGuid();
        (await Turnstiles().Autosave("u", id, TestTurnstile.UnlockedJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        (await Notes().Autosave("u", id, Note)).Should().BeOfType<AutosaveResult.Saved>();

        (await Turnstiles().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");
        (await Notes().Load("u", id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.Machine.Should()
            .Be("note");
    }

    [Test]
    public async Task One_machine_does_not_see_another_machines_draft()
    {
        var id = Guid.NewGuid();
        (await Notes().Autosave("u", id, Note)).Should().BeOfType<AutosaveResult.Saved>();

        (await Turnstiles().Load("u", id)).Should().BeOfType<LoadResult.NotFound>();
        (await Turnstiles().Advance("u", id, "Coin")).Should().BeOfType<AdvanceOutcome.NotFound>();
    }

    [Test]
    public async Task Expiring_one_machines_draft_leaves_another_machines_draft()
    {
        using var host = InstanceHost.Create(
            StoreProvider.Postgres,
            draftTtl: TimeSpan.FromMinutes(1)
        );
        var user = $"u-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        using (var scope = host.Scope())
        {
            (await host.Service(scope, "turnstile").Autosave(user, id, TestTurnstile.UnlockedJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
            (await host.Service(scope, "note").Autosave(user, id, Note))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        }
        // Only the turnstile's draft is idle past the TTL.
        await host.Execute(
            $"UPDATE {host.DraftTable} SET updated_at = @p0 WHERE id = @p1 AND machine = 'turnstile'",
            DateTimeOffset.UtcNow.AddHours(-1),
            id
        );

        await host.Services.GetRequiredService<DraftSweeper>().SweepOnce();

        using (var scope = host.Scope())
        {
            (await host.Service(scope, "turnstile").Load(user, id))
                .Should()
                .BeOfType<LoadResult.NotFound>();
            (await host.Service(scope, "note").Load(user, id))
                .Should()
                .BeOfType<LoadResult.Loaded>();
        }
    }
}
