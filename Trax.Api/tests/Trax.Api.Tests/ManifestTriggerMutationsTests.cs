using AwesomeAssertions;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// The manifest trigger mutations answer with what the operations service says the trigger did,
/// the call the dashboard's Trigger buttons make, so the two surfaces report the same thing.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class ManifestTriggerMutationsTests
{
    [Test]
    public async Task TriggerManifest_SaysWhatTheTriggerDid_AndNamesTheEntry()
    {
        var operations = Substitute.For<IOperationsService>();
        operations
            .TriggerManifestAsync("m", null, false, Arg.Any<CancellationToken>())
            .Returns(
                new TriggerManifestResult(
                    true,
                    "Manifest 'm' already had a queued run (work queue entry 9); the trigger brought it forward, now due now, and queued nothing more.",
                    new ManifestTriggerResult(9, false, null, false, null) { MovedForward = true }
                )
            );

        var response = await new OperationsMutations().TriggerManifest("m", operations, default);

        response.Success.Should().BeTrue();
        response.Id.Should().Be(9);
        response.Message.Should().Contain("brought it forward");
    }

    [Test]
    public async Task TriggerManifestDelayed_PassesTheDelay_AndAFailureChangesNothing()
    {
        var operations = Substitute.For<IOperationsService>();
        operations
            .TriggerManifestAsync("m", TimeSpan.FromMinutes(5), true, Arg.Any<CancellationToken>())
            .Returns(new TriggerManifestResult(false, OperationsService.NoDispatcherMessage, null));

        var response = await new OperationsMutations().TriggerManifestDelayed(
            "m",
            TimeSpan.FromMinutes(5),
            operations,
            default,
            askAfresh: true
        );

        response.Success.Should().BeFalse();
        response.Id.Should().BeNull();
        response.Message.Should().Be(OperationsService.NoDispatcherMessage);
    }

    [Test]
    public async Task TriggerGroup_CountsNewRuns_AndSaysWhatElseHappened()
    {
        var operations = Substitute.For<IOperationsService>();
        operations
            .TriggerManifestGroupsAsync(
                Arg.Is<IReadOnlyCollection<long>>(ids =>
                    ids != null && ids.SequenceEqual(new long[] { 7 })
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new BatchTriggerResult(
                    true,
                    1,
                    2,
                    1,
                    0,
                    0,
                    "2 queued, 1 already queued (that entry now runs as the trigger) across 1 of 1 manifest group(s).",
                    []
                )
            );

        var response = await new OperationsMutations().TriggerGroup(7, operations, default);

        response.Success.Should().BeTrue();
        response.Count.Should().Be(2);
        response.Message.Should().Contain("1 already queued");
    }
}
