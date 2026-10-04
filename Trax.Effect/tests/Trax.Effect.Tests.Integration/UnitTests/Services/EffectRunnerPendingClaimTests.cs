using AwesomeAssertions;
using NSubstitute;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRunner;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// An <see cref="IEffectRunner"/> written before pending runs were claimed does not implement
/// <see cref="IEffectRunner.TryClaimPendingRun"/>. It has nothing to claim through, so a run started
/// from a pre-created row goes ahead as it did before.
/// </summary>
[TestFixture]
public class EffectRunnerPendingClaimTests
{
    [Test]
    public async Task A_runner_that_cannot_claim_lets_a_pre_created_run_start()
    {
        IEffectRunner runner = new RunnerWithoutClaims();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Train",
                ExternalId = "ext",
                Input = null,
            }
        );

        (await runner.TryClaimPendingRun(metadata, CancellationToken.None)).Should().BeTrue();
    }

    /// <summary>
    /// Pins what the <see cref="IEffectRunner.TryClaimPendingRun"/> documentation warns about: a
    /// generated double does not inherit the default, so it refuses the claim until configured.
    /// </summary>
    [Test]
    public async Task A_substituted_runner_refuses_the_claim_until_it_is_configured()
    {
        var runner = Substitute.For<IEffectRunner>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Train",
                ExternalId = "ext",
                Input = null,
            }
        );

        (await runner.TryClaimPendingRun(metadata, CancellationToken.None)).Should().BeFalse();

        runner.TryClaimPendingRun(default!, default).ReturnsForAnyArgs(true);

        (await runner.TryClaimPendingRun(metadata, CancellationToken.None)).Should().BeTrue();
    }

    private sealed class RunnerWithoutClaims : IEffectRunner
    {
        public Task SaveChanges(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Track(IModel model) => Task.CompletedTask;

        public Task Update(IModel model) => Task.CompletedTask;

        public void Dispose() { }
    }
}
