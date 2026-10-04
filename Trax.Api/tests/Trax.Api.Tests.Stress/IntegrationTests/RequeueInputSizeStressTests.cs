using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.Tests.Stress.Fakes.Trains;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// <c>requeueExecution</c> reads the run's saved input and writes it into a new work queue entry
/// through the mediator, so its cost grows with the input as well as with the tables. Measured
/// against the same seed as <see cref="AdminMutationStressTests"/>, from 1 KiB to 1 MiB: an input
/// up to the mediator's input cap is re-queued whole, and one above it is refused before anything
/// is written, both within the list budget.
/// </summary>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class RequeueInputSizeStressTests : StressTestSetup
{
    private static readonly string ProbeTrainName = typeof(IStressProbeTrain).FullName!;

    // {"Value":"xxx…"}: 12 bytes of JSON around the value.
    private const int JsonOverhead = 12;

    private int InputCap => Services.GetRequiredService<MediatorConfiguration>().MaxInputJsonBytes;

    [TestCase(1024)]
    [TestCase(64 * 1024)]
    [TestCase(0, Description = "1 KiB under the mediator's input cap")]
    public async Task RequeueExecution_ASavedInputUpToTheCap_IsRequeuedWhole_WithinBudget(
        int inputBytes
    )
    {
        // The saved input is jsonb, which Postgres renders back with a space after each colon and
        // comma, so the input read back is a few bytes larger than the one written; 1 KiB of
        // headroom keeps the case under the cap whatever the rendering adds.
        if (inputBytes == 0)
            inputBytes = InputCap - 1024;

        var (response, queuedInputLength) = await MeasureRequeueAsync(inputBytes);

        response.Success.Should().BeTrue(response.Message);
        queuedInputLength
            .Should()
            .BeGreaterThanOrEqualTo(
                inputBytes - JsonOverhead,
                "the work queue entry carries the whole input"
            );
    }

    [Test]
    public async Task RequeueExecution_A1MiBSavedInput_IsRefusedAndWritesNothing_WithinBudget()
    {
        const int inputBytes = 1024 * 1024;
        inputBytes.Should().BeGreaterThan(InputCap, "this case measures an input over the cap");

        var (response, queuedInputLength) = await MeasureRequeueAsync(inputBytes);

        response.Success.Should().BeFalse();
        response.Message.Should().Be("The train input failed validation.");
        queuedInputLength.Should().Be(0, "a refused requeue writes no work queue entry");
    }

    /// <summary>
    /// Points one seeded run at the probe train with a saved input of <paramref name="inputBytes"/>,
    /// times its requeue, and returns the timed response and the length of the input the queued
    /// entry carries (0 when none was written). The run is put back afterwards.
    /// </summary>
    private async Task<(OperationResponse Response, long QueuedInputLength)> MeasureRequeueAsync(
        int inputBytes
    )
    {
        // A seeded run has no saved input and names a train nothing registers.
        var executionId = Profile.Metadata / 3 + 1;
        var seededName = await ScalarAsync<string>(
            $"SELECT name FROM trax.metadata WHERE id = {executionId}"
        );
        var input = $"{{\"Value\":\"{new string('x', inputBytes - JsonOverhead)}\"}}";
        input.Length.Should().Be(inputBytes);

        OperationResponse response = null!;
        long queuedInputLength = 0;
        await MeasureWriteAsync(
            $"operations.requeueExecution ({inputBytes / 1024} KiB input)",
            ListBudget,
            () =>
                ExecSqlAsync(
                    "DELETE FROM trax.work_queue WHERE status = 'queued' "
                        + $"AND train_name = '{ProbeTrainName}'; "
                        + $"UPDATE trax.metadata SET name = '{ProbeTrainName}', "
                        + $"input = '{input}' WHERE id = {executionId}"
                ),
            async (sp, ct) =>
                response = await new OperationsMutations().RequeueExecution(
                    executionId,
                    sp.GetRequiredService<IOperationsService>(),
                    ct
                ),
            restore: async () =>
            {
                queuedInputLength = await ScalarAsync<long>(
                    "SELECT coalesce(max(length(input::text)), 0) FROM trax.work_queue "
                        + $"WHERE status = 'queued' AND train_name = '{ProbeTrainName}'"
                );
                await ExecSqlAsync(
                    "DELETE FROM trax.work_queue WHERE status = 'queued' "
                        + $"AND train_name = '{ProbeTrainName}'; "
                        + $"UPDATE trax.metadata SET name = '{seededName}', input = NULL "
                        + $"WHERE id = {executionId}"
                );
            }
        );
        return (response, queuedInputLength);
    }
}
