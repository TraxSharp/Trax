using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.JunctionEffectRunner;
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Tests.Integration.Fakes.Trains;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A junction can identify the run it is executing in by primary key.
///
/// <para>The point is the ordering as much as the value: <c>ServiceTrain.Run</c> persists the
/// metadata row before the chain runs, so the identity column is already populated by the time
/// the first junction builds its <see cref="JunctionMetadata"/>. Against a real database that is
/// checkable; against an unpersisted metadata it is not, which is why this is an integration
/// test rather than a unit test.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class JunctionRunIdentityTests : TestSetup
{
    private RunIdProbeTrain BuildTrain(RunIdProbeJunction junction) =>
        new(junction)
        {
            EffectRunner = Scope.ServiceProvider.GetRequiredService<IEffectRunner>(),
            JunctionEffectRunner =
                Scope.ServiceProvider.GetRequiredService<IJunctionEffectRunner>(),
            LifecycleHookRunner = Scope.ServiceProvider.GetRequiredService<ILifecycleHookRunner>(),
            ServiceProvider = Scope.ServiceProvider,
        };

    [Test]
    public async Task Junction_SeesTheMetadataIdOfTheRunItExecutesIn()
    {
        var junction = new RunIdProbeJunction();
        var train = BuildTrain(junction);

        await train.Run(new RunIdProbeInput("run-identity"));

        train.Metadata!.Id.Should().BeGreaterThan(0, "the run was persisted to Postgres");
        junction
            .ObservedTrainMetadataId.Should()
            .Be(
                train.Metadata.Id,
                "the junction must be able to read its own run back by primary key"
            );
    }

    [Test]
    public async Task Junction_SeesTheIdInsideRun_NotOnlyAfterTheTrainFinishes()
    {
        // The value is captured inside Run, so a non-zero reading here is evidence that the
        // metadata row was already inserted before the chain started rather than after it.
        var junction = new RunIdProbeJunction();
        var train = BuildTrain(junction);

        await train.Run(new RunIdProbeInput("ordering"));

        junction.ObservedTrainMetadataId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task TwoRunsOfTheSameTrain_SeeDifferentMetadataIds()
    {
        // The reason TrainMetadataId exists rather than TrainExternalId being enough: a second
        // run is a second row. metadata.external_id has had no unique index since migration 005,
        // so only the id distinguishes two rows for the same logical piece of work.
        var first = new RunIdProbeJunction();
        var second = new RunIdProbeJunction();

        await BuildTrain(first).Run(new RunIdProbeInput("first"));
        await BuildTrain(second).Run(new RunIdProbeInput("second"));

        first.ObservedTrainMetadataId.Should().BeGreaterThan(0);
        second.ObservedTrainMetadataId.Should().NotBe(first.ObservedTrainMetadataId);
    }

    [Test]
    public async Task JunctionMetadata_CopiesTheIdOfAPersistedMetadataRow()
    {
        // The narrow check, independent of the train machinery: Create copies the id it is given.
        using var context = DataContextFactory.Create();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "JunctionRunIdentityTest",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        metadata.Id.Should().BeGreaterThan(0);

        var junctionMetadata = JunctionMetadata.Create(
            new CreateJunctionMetadata
            {
                Name = "Jx",
                ExternalId = Guid.NewGuid().ToString("N"),
                InputType = typeof(int),
                OutputType = typeof(string),
                State = EitherStatus.IsRight,
            },
            metadata
        );

        junctionMetadata.TrainMetadataId.Should().Be(metadata.Id);
        junctionMetadata.Id.Should().Be(0, "Id is the junction's own id and is never assigned");
    }
}
