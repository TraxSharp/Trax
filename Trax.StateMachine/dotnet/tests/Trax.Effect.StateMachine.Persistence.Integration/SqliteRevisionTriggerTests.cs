using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// SQLite's revision trigger (migration 038) numbers every update, and stops after its own write even when the
/// connection turns recursive triggers on: an update that leaves the revision where it was, or moves it back, ends one
/// past the stored revision, and one that moves it forward keeps its value, so the column never goes back.
/// </summary>
public class SqliteRevisionTriggerTests
{
    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(StoreProvider.Sqlite);

    [TearDown]
    public void TearDown() => _host.Dispose();

    private async Task<long> Seeded(Guid id)
    {
        await _host.SeedDraft("trigger-user", id, PlainTimerMachine.Json("Idle"));
        return await _host.RowId(id);
    }

    private Task<int> Update(string set, long rowId, params object?[] values) =>
        _host.Execute(
            $"PRAGMA recursive_triggers = ON; UPDATE snapshot_draft SET {set} WHERE row_id = @p0",
            [rowId, .. values]
        );

    [Test]
    public async Task An_update_that_leaves_the_revision_is_bumped_once_with_recursive_triggers_on()
    {
        var id = Guid.NewGuid();
        var row = await Seeded(id);
        var before = await _host.Revision(id);

        await Update("context = '{}'", row);

        (await _host.Revision(id)).Should().Be(before + 1);
    }

    [Test]
    public async Task An_update_that_moves_the_revision_back_ends_one_past_it_with_recursive_triggers_on()
    {
        var id = Guid.NewGuid();
        var row = await Seeded(id);
        await Update("context = '{}'", row);
        await Update("context = '{}'", row);
        var before = await _host.Revision(id);

        await Update("revision = @p1", row, 1L);

        (await _host.Revision(id))
            .Should()
            .Be(
                before + 1,
                "the trigger replaces a revision that goes back, and stops after its own write"
            );
    }

    [Test]
    public async Task An_update_that_moves_the_revision_forward_keeps_it_with_recursive_triggers_on()
    {
        var id = Guid.NewGuid();
        var row = await Seeded(id);

        await Update("revision = @p1", row, 99L);

        (await _host.Revision(id)).Should().Be(99, "a revision moved forward never goes back");
    }
}
