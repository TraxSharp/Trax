using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E.MaterialsApi;

[TestFixture]
public class GenerateMaterialsTests : JobHuntApiTestFixture
{
    private async Task<string> SetupJobAndProfile()
    {
        // Create profile
        await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                updateProfile(input: {
                  userId: "alice",
                  facet: SKILLS,
                  json: "{{EscapeJson("""["C#", "Go"]""")}}"
                }) { output { userId } }
              }
            }
            """,
            apiKey: AliceKey
        );

        // Create job
        var jobResult = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "alice",
                  pastedTitle: "Senior Engineer",
                  pastedCompany: "Acme",
                  pastedDescription: "Build distributed systems"
                }) {
                  output { jobId }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        return jobResult.GetData("dispatch", "addJob", "output", "jobId").GetString()!;
    }

    [Test]
    public async Task Generate_ReturnsMaterials()
    {
        var jobId = await SetupJobAndProfile();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                generateApplicationMaterials(input: {
                  userId: "alice",
                  jobId: "{{jobId}}"
                }) {
                  output {
                    resumeArtifactId
                    coverLetterArtifactId
                    resumeMarkdown
                    coverLetterMarkdown
                  }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var output = result.GetData("dispatch", "generateApplicationMaterials", "output");
        output.GetProperty("resumeArtifactId").GetString().Should().NotBeNullOrEmpty();
        output.GetProperty("coverLetterArtifactId").GetString().Should().NotBeNullOrEmpty();
        output.GetProperty("resumeMarkdown").GetString().Should().Contain("Generated with");
        output.GetProperty("coverLetterMarkdown").GetString().Should().Contain("Generated with");
    }

    [Test]
    public async Task Generate_VisibleViaGetArtifacts()
    {
        var jobId = await SetupJobAndProfile();

        await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                generateApplicationMaterials(input: {
                  userId: "alice",
                  jobId: "{{jobId}}"
                }) { output { resumeArtifactId } }
              }
            }
            """,
            apiKey: AliceKey
        );

        var result = await GraphQL.SendAsync(
            $$"""
            {
              discover {
                getArtifacts(input: { jobId: "{{jobId}}", userId: "alice" }) {
                  artifacts { id type content modelUsed }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var artifacts = result.GetData("discover", "getArtifacts", "artifacts");
        artifacts.GetArrayLength().Should().Be(2);
    }

    [Test]
    public async Task Generate_NoProfile_ReturnsError()
    {
        // Create job but no profile
        var jobResult = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "alice",
                  pastedTitle: "Dev",
                  pastedCompany: "Co",
                  pastedDescription: "Work"
                }) { output { jobId } }
              }
            }
            """,
            apiKey: AliceKey
        );
        var jobId = jobResult.GetData("dispatch", "addJob", "output", "jobId").GetString()!;

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                generateApplicationMaterials(input: {
                  userId: "alice",
                  jobId: "{{jobId}}"
                }) { output { resumeArtifactId } }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeTrue();
    }

    private static string EscapeJson(string json) =>
        json.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
