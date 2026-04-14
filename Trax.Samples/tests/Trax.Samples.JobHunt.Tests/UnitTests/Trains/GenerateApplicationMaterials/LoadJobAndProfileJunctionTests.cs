using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;
using Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.GenerateApplicationMaterials;

[TestFixture]
public class LoadJobAndProfileJunctionTests
{
    [Test]
    public async Task Run_BothExist_ReturnsCombinedContext()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var jobId = Guid.NewGuid();
        db.Jobs.Add(
            new Job
            {
                Id = jobId,
                UserId = "alice",
                Title = "Engineer",
                Company = "Acme",
                RawDescription = "Build things",
                Status = JobStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        db.Profiles.Add(
            new Profile
            {
                Id = Guid.NewGuid(),
                UserId = "alice",
                SkillsJson = """["C#"]""",
                EducationJson = """["MIT"]""",
                WorkHistoryJson = """[{"co":"Acme"}]""",
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var junction = new LoadJobAndProfileJunction(db);
        var result = await junction.Run(
            new GenerateApplicationMaterialsInput { JobId = jobId, UserId = "alice" }
        );

        result.JobTitle.Should().Be("Engineer");
        result.JobCompany.Should().Be("Acme");
        result.SkillsJson.Should().Be("""["C#"]""");
    }

    [Test]
    public void Run_JobMissing_Throws()
    {
        using var db = JobHuntDbContextFixture.Create();
        db.Profiles.Add(
            new Profile
            {
                Id = Guid.NewGuid(),
                UserId = "alice",
                UpdatedAt = DateTime.UtcNow,
            }
        );
        db.SaveChanges();

        var junction = new LoadJobAndProfileJunction(db);
        var act = () =>
            junction.Run(
                new GenerateApplicationMaterialsInput { JobId = Guid.NewGuid(), UserId = "alice" }
            );

        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Test]
    public void Run_ProfileMissing_Throws()
    {
        using var db = JobHuntDbContextFixture.Create();
        var jobId = Guid.NewGuid();
        db.Jobs.Add(
            new Job
            {
                Id = jobId,
                UserId = "alice",
                Title = "Dev",
                Company = "Co",
                RawDescription = "Work",
                Status = JobStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        db.SaveChanges();

        var junction = new LoadJobAndProfileJunction(db);
        var act = () =>
            junction.Run(new GenerateApplicationMaterialsInput { JobId = jobId, UserId = "alice" });

        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Profile*not found*");
    }
}
