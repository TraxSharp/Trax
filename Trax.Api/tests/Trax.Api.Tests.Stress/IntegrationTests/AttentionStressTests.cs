using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// <c>operations.attention</c> at millions of rows, against the same seed as
/// <see cref="AdminEndpointStressTests"/>: half a million dead letters awaiting intervention, a
/// week of runs, a million and a half queue entries and two million state-machine instances, twenty
/// thousand of them waiting on a run.
/// </summary>
/// <remarks>
/// The slowest of its reads is the dead letters awaiting intervention grouped by train and reason:
/// the seed gives each of its half million its own reason, so the grouping holds half a million
/// groups (about 480 ms on its own). The scheduler never writes that: it dead-letters a manifest only
/// when the manifest has no dead letter awaiting, so a real host has at most one per manifest. The
/// budget is set over that worst case rather than the metrics block's.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class AttentionStressTests : StressTestSetup
{
    private static readonly TimeSpan AttentionBudget = TimeSpan.FromMilliseconds(750);

    [Test]
    public Task Attention_AtScale_WithinBudget() =>
        MeasureAsync(
            "operations.attention",
            AttentionBudget,
            async (sp, ct) =>
            {
                var attention = await new OperationsQueries().GetAttention(
                    sp.GetRequiredService<IOperationsService>(),
                    ct
                );
                attention.DeadLetters.Should().HaveCount(OperationsService.AttentionListCap);
                attention
                    .DeadLetters.Count.Should()
                    .BeLessThanOrEqualTo(OperationsPageBounds.MaxPageSize);
                attention.Queue.Queued.Should().BePositive();
            }
        );
}
