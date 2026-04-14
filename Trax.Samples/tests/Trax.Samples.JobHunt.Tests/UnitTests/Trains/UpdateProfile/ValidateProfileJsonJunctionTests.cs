using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Trains.UpdateProfile;
using Trax.Samples.JobHunt.Trains.UpdateProfile.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.UpdateProfile;

[TestFixture]
public class ValidateProfileJsonJunctionTests
{
    private readonly ValidateProfileJsonJunction _junction = new();

    [Test]
    public async Task Run_ValidJsonArray_ReturnsInput()
    {
        var input = new UpdateProfileInput
        {
            UserId = "alice",
            Facet = ProfileFacet.Skills,
            Json = """["C#", "TypeScript", "Postgres"]""",
        };

        var result = await _junction.Run(input);

        result.Should().BeSameAs(input);
    }

    [Test]
    public void Run_InvalidJson_Throws()
    {
        var input = new UpdateProfileInput
        {
            UserId = "alice",
            Facet = ProfileFacet.Skills,
            Json = "not json at all",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*Invalid JSON*");
    }

    [Test]
    public void Run_JsonObject_Throws()
    {
        var input = new UpdateProfileInput
        {
            UserId = "alice",
            Facet = ProfileFacet.Skills,
            Json = """{"skill": "C#"}""",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*array*");
    }

    [Test]
    public void Run_EmptyJson_Throws()
    {
        var input = new UpdateProfileInput
        {
            UserId = "alice",
            Facet = ProfileFacet.Skills,
            Json = "",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*required*");
    }

    [Test]
    public void Run_EmptyUserId_Throws()
    {
        var input = new UpdateProfileInput
        {
            UserId = "",
            Facet = ProfileFacet.Skills,
            Json = "[]",
        };

        var act = () => _junction.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*UserId*");
    }

    [Test]
    public async Task Run_EmptyArray_Succeeds()
    {
        var input = new UpdateProfileInput
        {
            UserId = "alice",
            Facet = ProfileFacet.Education,
            Json = "[]",
        };

        var result = await _junction.Run(input);

        result.Should().BeSameAs(input);
    }
}
