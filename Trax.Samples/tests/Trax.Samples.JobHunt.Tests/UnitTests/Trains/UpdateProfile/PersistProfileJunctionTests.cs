using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.UpdateProfile;
using Trax.Samples.JobHunt.Trains.UpdateProfile.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.UpdateProfile;

[TestFixture]
public class PersistProfileJunctionTests
{
    [Test]
    public async Task Run_FirstWrite_CreatesProfile()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistProfileJunction(db, NullLogger<PersistProfileJunction>.Instance);

        var input = new UpdateProfileInput
        {
            UserId = "alice",
            Facet = ProfileFacet.Skills,
            Json = """["C#", "Go"]""",
        };

        await junction.Run(input);

        var profile = await db.Profiles.SingleAsync();
        profile.UserId.Should().Be("alice");
        profile.SkillsJson.Should().Be("""["C#", "Go"]""");
        profile.EducationJson.Should().Be("[]");
        profile.WorkHistoryJson.Should().Be("[]");
    }

    [Test]
    public async Task Run_SecondWrite_UpdatesExisting()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistProfileJunction(db, NullLogger<PersistProfileJunction>.Instance);

        await junction.Run(
            new UpdateProfileInput
            {
                UserId = "alice",
                Facet = ProfileFacet.Skills,
                Json = """["C#"]""",
            }
        );

        await junction.Run(
            new UpdateProfileInput
            {
                UserId = "alice",
                Facet = ProfileFacet.Skills,
                Json = """["C#", "Go", "Rust"]""",
            }
        );

        var profiles = await db.Profiles.ToListAsync();
        profiles.Should().ContainSingle();
        profiles[0].SkillsJson.Should().Be("""["C#", "Go", "Rust"]""");
    }

    [Test]
    public async Task Run_DifferentFacets_UpdateIndependently()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistProfileJunction(db, NullLogger<PersistProfileJunction>.Instance);

        await junction.Run(
            new UpdateProfileInput
            {
                UserId = "alice",
                Facet = ProfileFacet.Skills,
                Json = """["C#"]""",
            }
        );

        await junction.Run(
            new UpdateProfileInput
            {
                UserId = "alice",
                Facet = ProfileFacet.Education,
                Json = """["MIT"]""",
            }
        );

        var profile = await db.Profiles.SingleAsync();
        profile.SkillsJson.Should().Be("""["C#"]""");
        profile.EducationJson.Should().Be("""["MIT"]""");
    }

    [Test]
    public async Task Run_ReturnsCorrectOutput()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistProfileJunction(db, NullLogger<PersistProfileJunction>.Instance);

        var result = await junction.Run(
            new UpdateProfileInput
            {
                UserId = "alice",
                Facet = ProfileFacet.WorkHistory,
                Json = """[{"company":"Acme"}]""",
            }
        );

        result.UserId.Should().Be("alice");
        result.Facet.Should().Be(ProfileFacet.WorkHistory);
        result.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
