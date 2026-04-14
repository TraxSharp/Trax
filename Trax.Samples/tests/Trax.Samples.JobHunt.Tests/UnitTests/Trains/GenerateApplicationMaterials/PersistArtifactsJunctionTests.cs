using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;
using Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.GenerateApplicationMaterials;

[TestFixture]
public class PersistArtifactsJunctionTests
{
    [Test]
    public async Task Run_WritesBothArtifacts()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistArtifactsJunction(
            db,
            NullLogger<PersistArtifactsJunction>.Instance
        );

        var ctx = MakeContext();
        var result = await junction.Run(ctx);

        var artifacts = await db.Artifacts.ToListAsync();
        artifacts.Should().HaveCount(2);
        artifacts.Should().Contain(a => a.Type == ArtifactType.Resume);
        artifacts.Should().Contain(a => a.Type == ArtifactType.CoverLetter);
    }

    [Test]
    public async Task Run_TaggedWithCorrectModelName()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistArtifactsJunction(
            db,
            NullLogger<PersistArtifactsJunction>.Instance
        );

        var result = await junction.Run(MakeContext());

        var resume = await db.Artifacts.FirstAsync(a => a.Type == ArtifactType.Resume);
        resume.ModelUsed.Should().Be("test-model");

        var cover = await db.Artifacts.FirstAsync(a => a.Type == ArtifactType.CoverLetter);
        cover.ModelUsed.Should().Be("test-model-cl");
    }

    [Test]
    public async Task Run_ReturnsOutputWithIds()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistArtifactsJunction(
            db,
            NullLogger<PersistArtifactsJunction>.Instance
        );

        var result = await junction.Run(MakeContext());

        result.ResumeArtifactId.Should().NotBeEmpty();
        result.CoverLetterArtifactId.Should().NotBeEmpty();
        result.ResumeMarkdown.Should().Be("# Resume");
        result.CoverLetterMarkdown.Should().Be("# Cover Letter");
    }

    private static MaterialsContext MakeContext() =>
        new()
        {
            Input = new GenerateApplicationMaterialsInput
            {
                JobId = Guid.NewGuid(),
                UserId = "alice",
            },
            JobTitle = "Engineer",
            JobCompany = "Acme",
            JobDescription = "Build things",
            SkillsJson = "[]",
            EducationJson = "[]",
            WorkHistoryJson = "[]",
            ResumeMarkdown = "# Resume",
            ResumeModel = "test-model",
            CoverLetterMarkdown = "# Cover Letter",
            CoverLetterModel = "test-model-cl",
        };
}
