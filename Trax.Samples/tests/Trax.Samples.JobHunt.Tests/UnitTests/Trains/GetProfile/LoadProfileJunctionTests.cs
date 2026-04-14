using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.GetProfile;
using Trax.Samples.JobHunt.Trains.GetProfile.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.GetProfile;

[TestFixture]
public class LoadProfileJunctionTests
{
    [Test]
    public async Task Run_NoProfile_ReturnsDefaults()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new LoadProfileJunction(db);

        var result = await junction.Run(new GetProfileInput { UserId = "alice" });

        result.UserId.Should().Be("alice");
        result.SkillsJson.Should().Be("[]");
        result.EducationJson.Should().Be("[]");
        result.WorkHistoryJson.Should().Be("[]");
    }

    [Test]
    public async Task Run_ExistingProfile_ReturnsStoredValues()
    {
        await using var db = JobHuntDbContextFixture.Create();
        db.Profiles.Add(
            new Profile
            {
                Id = Guid.NewGuid(),
                UserId = "alice",
                SkillsJson = """["C#"]""",
                EducationJson = """["MIT"]""",
                WorkHistoryJson = """[{"company":"Acme"}]""",
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var junction = new LoadProfileJunction(db);
        var result = await junction.Run(new GetProfileInput { UserId = "alice" });

        result.SkillsJson.Should().Be("""["C#"]""");
        result.EducationJson.Should().Be("""["MIT"]""");
        result.WorkHistoryJson.Should().Be("""[{"company":"Acme"}]""");
    }

    [Test]
    public async Task Run_DifferentUsers_AreIsolated()
    {
        await using var db = JobHuntDbContextFixture.Create();
        db.Profiles.Add(
            new Profile
            {
                Id = Guid.NewGuid(),
                UserId = "alice",
                SkillsJson = """["Alice skills"]""",
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var junction = new LoadProfileJunction(db);
        var result = await junction.Run(new GetProfileInput { UserId = "bob" });

        result.SkillsJson.Should().Be("[]");
    }
}
