using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E.ApplicationsApi;

[TestFixture]
public class ApplicationTests : JobHuntApiTestFixture
{
    [Test]
    public async Task CreateApplication_ReturnsApplicationId()
    {
        var jobId = await AddTestJob();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                createApplication(input: {
                  userId: "alice",
                  jobId: "{{jobId}}"
                }) {
                  output { applicationId status }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var output = result.GetData("dispatch", "createApplication", "output");
        output.GetProperty("applicationId").GetString().Should().NotBeNullOrEmpty();
        output.GetProperty("status").GetString().Should().Be("Drafted");
    }

    [Test]
    public async Task ListApplications_AfterCreate_ReturnsApplication()
    {
        var jobId = await AddTestJob();

        await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                createApplication(input: { userId: "alice", jobId: "{{jobId}}" }) {
                  output { applicationId }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        var result = await GraphQL.SendAsync(
            """
            {
              discover {
                listApplications(input: { userId: "alice" }) {
                  applications { id jobId status }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var apps = result.GetData("discover", "listApplications", "applications");
        apps.GetArrayLength().Should().Be(1);
        apps[0].GetProperty("status").GetString().Should().Be("Drafted");
    }

    [Test]
    public async Task FindContact_PersistsContact()
    {
        var jobId = await AddTestJob();

        var result = await GraphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                findContact(input: {
                  jobId: "{{jobId}}",
                  name: "Jane Recruiter",
                  email: "jane@acme.com"
                }) {
                  output { contactId name email source }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var output = result.GetData("dispatch", "findContact", "output");
        output.GetProperty("name").GetString().Should().Be("Jane Recruiter");
        output.GetProperty("email").GetString().Should().Be("jane@acme.com");
        output.GetProperty("source").GetString().Should().Be("Manual");
    }

    private async Task<string> AddTestJob()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "alice",
                  pastedTitle: "Engineer",
                  pastedCompany: "Acme",
                  pastedDescription: "Work"
                }) { output { jobId } }
              }
            }
            """,
            apiKey: AliceKey
        );
        return result.GetData("dispatch", "addJob", "output", "jobId").GetString()!;
    }
}
