using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The operator listing of state-machine instances the dashboard and the GraphQL API share, on
/// Sqlite: newest first by when each was last written, which Sqlite compares as fixed-width text,
/// filtered by owner kind, with the counts by state and the owner-kind lookup as on Postgres.
/// </summary>
[TestFixture]
public class SqliteMachineInstanceQueryTests : TestSetup
{
    private IOperationsService _operations = null!;
    private string _machine = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
        _machine = $"sqlite-machine-{Guid.NewGuid():N}";
    }

    [Test]
    public async Task Instances_list_newest_first_and_the_lookup_names_the_owner_kind()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var system = await Seed(SnapshotOwnerKind.System, "Running", at, id);
        var user = await Seed(SnapshotOwnerKind.User, "Running", at.AddSeconds(1), id);
        await Seed(SnapshotOwnerKind.System, "Done", at.AddSeconds(2), Guid.NewGuid());

        var page = await _operations.GetMachineInstancesAsync(
            new MachineInstanceQuery(_machine, "Running"),
            CancellationToken.None
        );
        var systemOnly = await _operations.GetMachineInstancesAsync(
            new MachineInstanceQuery(_machine, OwnerKind: SnapshotOwnerKind.System),
            CancellationToken.None
        );
        var counts = await _operations.GetMachineInstanceStateCountsAsync(
            _machine,
            CancellationToken.None
        );
        var looked = await _operations.GetMachineInstanceAsync(
            new MachineInstanceKey(_machine, SnapshotOwnerKind.System, id),
            CancellationToken.None
        );

        page.Items.Select(i => i.RowId).Should().Equal(user.RowId, system.RowId);
        systemOnly
            .Items.Should()
            .HaveCount(2)
            .And.OnlyContain(i => i.OwnerKind == SnapshotOwnerKind.System);
        counts
            .Select(c => (c.State, c.OwnerKind, c.Count))
            .Should()
            .Equal(
                ("Done", SnapshotOwnerKind.System, 1L),
                ("Running", SnapshotOwnerKind.User, 1L),
                ("Running", SnapshotOwnerKind.System, 1L)
            );
        looked!.RowId.Should().Be(system.RowId);
        looked.CreatedAt.Should().Be(at);
    }

    private async Task<SnapshotDraft> Seed(
        SnapshotOwnerKind owner,
        string state,
        DateTimeOffset updatedAt,
        Guid id
    )
    {
        var row = new SnapshotDraft
        {
            Id = id,
            OwnerKind = owner,
            UserKey = owner == SnapshotOwnerKind.User ? "sqlite-user" : null,
            Machine = _machine,
            Version = 1,
            State = state,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
        };
        var context = (DbContext)DataContext;
        context.Add(row);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return row;
    }
}
