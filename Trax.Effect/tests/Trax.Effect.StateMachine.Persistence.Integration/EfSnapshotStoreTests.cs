using System.Text.Json.Nodes;
using FluentAssertions;
using Npgsql;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public class EfSnapshotStoreTests
{
    private static Snapshot Unlocked(string paidWith = "quarter") =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = paidWith },
        };

    private static async Task<string?> Scalar(string sql)
    {
        await using var conn = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result?.ToString();
    }

    [Test]
    public async Task Round_trips_through_a_real_jsonb_column_queryable_server_side()
    {
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert("u1", id, Unlocked())).Should().BeTrue();

        (
            await Scalar(
                $"SELECT context->>'paidWith' FROM trax.snapshot_draft WHERE id = '{id}' AND user_key = 'u1'"
            )
        )
            .Should()
            .Be("quarter");

        (
            await Scalar(
                "SELECT data_type FROM information_schema.columns "
                    + "WHERE table_schema='trax' AND table_name='snapshot_draft' AND column_name='context'"
            )
        )
            .Should()
            .Be("jsonb");
    }

    [Test]
    public async Task A_draft_is_isolated_to_its_owner()
    {
        var id = Guid.NewGuid();
        await TestDb.NewStore().Upsert("owner", id, Unlocked());

        (await TestDb.NewStore().Get("owner", id)).Should().NotBeNull();
        (await TestDb.NewStore().Get("someone-else", id)).Should().BeNull();
    }

    [Test]
    public async Task Two_users_may_hold_the_same_id_as_separate_rows()
    {
        var id = Guid.NewGuid();

        (await TestDb.NewStore().Upsert("user-a", id, Unlocked("quarter"))).Should().BeTrue();
        (await TestDb.NewStore().Upsert("user-b", id, Unlocked("dollar"))).Should().BeTrue();

        (await TestDb.NewStore().Get("user-a", id))!.Json.Should().Contain("quarter");
        (await TestDb.NewStore().Get("user-b", id))!.Json.Should().Contain("dollar");
    }

    [Test]
    public async Task Delete_removes_the_row()
    {
        var id = Guid.NewGuid();
        await TestDb.NewStore().Upsert("u", id, Unlocked());
        (await TestDb.NewStore().Get("u", id)).Should().NotBeNull();

        await TestDb.NewStore().Delete("u", id);

        (await TestDb.NewStore().Get("u", id)).Should().BeNull();
    }

    [Test]
    public async Task Delete_is_idempotent_on_a_missing_row()
    {
        var act = async () => await TestDb.NewStore().Delete("u", Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Get_surfaces_the_rows_updated_at()
    {
        var id = Guid.NewGuid();
        await TestDb.NewStore().Upsert("u", id, Unlocked());

        (await TestDb.NewStore().Get("u", id))!
            .UpdatedAt.Should()
            .BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task Update_applies_only_when_the_concurrency_token_still_matches()
    {
        var id = Guid.NewGuid();
        await TestDb.NewStore().Upsert("u", id, Unlocked("quarter"));
        var stored = await TestDb.NewStore().Get("u", id);

        // A stale token loses.
        (await TestDb.NewStore().Update("u", id, Unlocked("dollar"), Guid.NewGuid()))
            .Should()
            .BeFalse();
        // The current token wins.
        (await TestDb.NewStore().Update("u", id, Unlocked("dollar"), stored!.Token))
            .Should()
            .BeTrue();
        (await TestDb.NewStore().Get("u", id))!.Json.Should().Contain("dollar");
    }
}
