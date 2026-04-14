using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E.JobsApi;

[TestFixture]
public class ListJobsTests : JobHuntApiTestFixture
{
    [Test]
    public async Task ListJobs_EmptyUser_ReturnsEmpty()
    {
        var result = await GraphQL.SendAsync(
            """
            {
              discover {
                listJobs(input: { userId: "alice" }) {
                  jobs { id title }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var jobs = result.GetData("discover", "listJobs", "jobs");
        jobs.GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task ListJobs_AfterAddJob_ReturnsAddedJob()
    {
        // Add a job first
        await GraphQL.SendAsync(
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

        var result = await GraphQL.SendAsync(
            """
            {
              discover {
                listJobs(input: { userId: "alice" }) {
                  jobs { id title company status }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var jobs = result.GetData("discover", "listJobs", "jobs");
        jobs.GetArrayLength().Should().Be(1);
        jobs[0].GetProperty("title").GetString().Should().Be("Engineer");
        jobs[0].GetProperty("company").GetString().Should().Be("Acme");
        jobs[0].GetProperty("status").GetString().Should().Be("Active");
    }

    [Test]
    public async Task ListJobs_DoesNotLeakAcrossUsers()
    {
        await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "alice",
                  pastedTitle: "Alice Job",
                  pastedCompany: "AliceCo",
                  pastedDescription: "Alice work"
                }) { output { jobId } }
              }
            }
            """,
            apiKey: AliceKey
        );

        var result = await GraphQL.SendAsync(
            """
            {
              discover {
                listJobs(input: { userId: "bob" }) {
                  jobs { id title }
                }
              }
            }
            """,
            apiKey: BobKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        result.GetData("discover", "listJobs", "jobs").GetArrayLength().Should().Be(0);
    }
}
