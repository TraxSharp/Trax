using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// Pass-through tests for the GraphQL <c>operations.config</c> namespace. Behavioural
/// tests for the underlying <see cref="IOperationsService"/> live in
/// <c>Trax.Scheduler.Tests.Integration.OperationsServiceConfigTests</c>.
/// </summary>
[TestFixture]
public class ConfigOperationsTests
{
    [Test]
    public void GetScheduler_ForwardsToOperationsService()
    {
        var ops = Substitute.For<IOperationsService>();
        var snap = new SchedulerConfigSnapshot(
            ManifestManagerEnabled: true,
            JobDispatcherEnabled: true,
            ManifestManagerPollingInterval: TimeSpan.FromSeconds(5),
            JobDispatcherPollingInterval: TimeSpan.FromSeconds(2),
            MaxActiveJobs: 10,
            DefaultMaxRetries: 3,
            DefaultRetryDelay: TimeSpan.FromMinutes(5),
            RetryBackoffMultiplier: 2.0,
            MaxRetryDelay: TimeSpan.FromHours(1),
            DefaultJobTimeout: TimeSpan.FromMinutes(20),
            StalePendingTimeout: TimeSpan.FromMinutes(20),
            RecoverStuckJobsOnStartup: true,
            DeadLetterRetentionPeriod: TimeSpan.FromDays(30),
            AutoPurgeDeadLetters: true,
            LocalWorkerCount: 4,
            MetadataCleanupInterval: null,
            MetadataCleanupRetention: null
        );
        ops.GetSchedulerConfig().Returns(snap);
        var queries = new ConfigQueries();

        queries.GetScheduler(ops).Should().BeSameAs(snap);
    }

    [Test]
    public async Task UpdateScheduler_ForwardsToOperationsService_AndMapsSuccess()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.UpdateSchedulerConfigAsync(
                Arg.Any<UpdateSchedulerConfigInput>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OperationResult(true, Id: 1, Count: 3, Message: "ok"));
        var mutations = new ConfigMutations();
        var input = new UpdateSchedulerConfigInput(MaxActiveJobs: 50);

        var response = await mutations.UpdateScheduler(input, ops, default);

        response.Success.Should().BeTrue();
        response.Count.Should().Be(3);
        response.Message.Should().Be("ok");
        await ops.Received(1).UpdateSchedulerConfigAsync(input, Arg.Any<CancellationToken>());
    }

    [Test]
    public void OperationsQueries_ConfigNamespace_ReturnsNewInstance()
    {
        new OperationsQueries().Config().Should().NotBeNull();
    }

    [Test]
    public void OperationsMutations_ConfigNamespace_ReturnsNewInstance()
    {
        new OperationsMutations().Config().Should().NotBeNull();
    }
}
