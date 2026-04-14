using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E.ProfileApi;

[TestFixture]
public class ProfileTests : JobHuntApiTestFixture
{
    [Test]
    public async Task GetProfile_NoProfile_ReturnsDefaults()
    {
        var result = await GraphQL.SendAsync(
            """
            {
              discover {
                getProfile(input: { userId: "alice" }) {
                  userId
                  skillsJson
                  educationJson
                  workHistoryJson
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var profile = result.GetData("discover", "getProfile");
        profile.GetProperty("userId").GetString().Should().Be("alice");
        profile.GetProperty("skillsJson").GetString().Should().Be("[]");
        profile.GetProperty("educationJson").GetString().Should().Be("[]");
        profile.GetProperty("workHistoryJson").GetString().Should().Be("[]");
    }

    [Test]
    public async Task UpdateProfile_Skills_PersistsAndReturnsOnGet()
    {
        var skillsJson = """["C#", "TypeScript", "Postgres"]""";

        var updateResult = await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                updateProfile(input: {
                  userId: "alice",
                  facet: SKILLS,
                  json: "{{EscapeJson(skillsJson)}}"
                }) {
                  output { userId facet updatedAt }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        updateResult.HasErrors.Should().BeFalse(updateResult.FirstErrorMessage);
        var output = updateResult.GetData("dispatch", "updateProfile", "output");
        output.GetProperty("facet").GetString().Should().Be("SKILLS");

        var getResult = await GraphQL.SendAsync(
            """
            {
              discover {
                getProfile(input: { userId: "alice" }) { skillsJson }
              }
            }
            """,
            apiKey: AliceKey
        );

        getResult.HasErrors.Should().BeFalse(getResult.FirstErrorMessage);
        getResult
            .GetData("discover", "getProfile", "skillsJson")
            .GetString()
            .Should()
            .Be(skillsJson);
    }

    [Test]
    public async Task UpdateProfile_InvalidJson_ReturnsGraphQLError()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                updateProfile(input: {
                  userId: "alice",
                  facet: SKILLS,
                  json: "not valid json"
                }) {
                  output { userId }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeTrue();
    }

    [Test]
    public async Task UpdateProfile_MultipleFacets_IndependentlyStored()
    {
        await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                updateProfile(input: {
                  userId: "alice",
                  facet: SKILLS,
                  json: "{{EscapeJson("""["C#"]""")}}"
                }) { output { userId } }
              }
            }
            """,
            apiKey: AliceKey
        );

        await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                updateProfile(input: {
                  userId: "alice",
                  facet: EDUCATION,
                  json: "{{EscapeJson("""["MIT"]""")}}"
                }) { output { userId } }
              }
            }
            """,
            apiKey: AliceKey
        );

        var getResult = await GraphQL.SendAsync(
            """
            {
              discover {
                getProfile(input: { userId: "alice" }) {
                  skillsJson
                  educationJson
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        getResult.HasErrors.Should().BeFalse(getResult.FirstErrorMessage);
        var profile = getResult.GetData("discover", "getProfile");
        profile.GetProperty("skillsJson").GetString().Should().Be("""["C#"]""");
        profile.GetProperty("educationJson").GetString().Should().Be("""["MIT"]""");
    }

    private static string EscapeJson(string json) =>
        json.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
