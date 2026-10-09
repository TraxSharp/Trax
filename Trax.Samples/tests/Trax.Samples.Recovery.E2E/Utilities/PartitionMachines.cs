using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Samples.Recovery.Machines;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// Reads the <c>source-partition</c> instances straight from <c>trax.snapshot_draft</c>, the way a test
/// checks what the machines did, and waits for their ingest runs to finish. The instances are keyed by
/// partition, so they are shared by every test that runs discovery or ingests a partition.
/// </summary>
public static class PartitionMachines
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Every instance of the machine, by partition.</summary>
    public static async Task<List<SnapshotDraft>> AllAsync()
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .Where(x =>
                x.Machine == SourcePartitionMachine.MachineId
                && x.OwnerKind == SnapshotOwnerKind.System
            )
            .ToListAsync();
    }

    /// <summary>The instance for one partition, or null.</summary>
    public static async Task<SnapshotDraft?> OfAsync(string source, string month)
    {
        var id = Effect.StateMachine.Persistence.MachineInstanceId.For(
            SourcePartitionMachine.MachineId,
            SourcePartitionMachine.KeyFor(source, month)
        );
        return (await AllAsync()).SingleOrDefault(x => x.Id == id);
    }

    /// <summary>Waits until the partition's instance is in <paramref name="state"/> with no live run.</summary>
    public static async Task<SnapshotDraft> WaitForStateAsync(
        string source,
        string month,
        string state
    )
    {
        SnapshotDraft? row = null;
        var reached = await Polling.WaitUntilAsync(
            async () =>
            {
                row = await OfAsync(source, month);
                return row is { InvokeToken: null } && row.State == state;
            },
            Timeout,
            TimeSpan.FromMilliseconds(100)
        );
        reached
            .Should()
            .BeTrue(
                $"{source}/{month} should reach {state}; it is in {row?.State ?? "no instance"}"
            );
        return row!;
    }

    /// <summary>
    /// Waits until no instance has a live ingest run, so a test that runs the ingest itself, or arms its
    /// crash, does not race a run a machine queued.
    /// </summary>
    public static async Task WaitUntilSettledAsync()
    {
        var settled = await Polling.WaitUntilAsync(
            async () => (await AllAsync()).All(x => x.InvokeToken is null),
            Timeout,
            TimeSpan.FromMilliseconds(100)
        );
        settled.Should().BeTrue("every partition instance's ingest run should finish");
    }

    /// <summary>
    /// Removes every instance once their runs have finished, so a test starts from no instances. The
    /// test database outlives a run of the suite, and these rows are keyed by partition.
    /// </summary>
    public static async Task ResetAsync()
    {
        await WaitUntilSettledAsync();
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.Where(x =>
                x.Machine == SourcePartitionMachine.MachineId
                && x.OwnerKind == SnapshotOwnerKind.System
            )
            .ExecuteDeleteAsync();
    }
}
