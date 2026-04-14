using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E.JobsApi;

[TestFixture]
public class AddJobTests : JobHuntApiTestFixture
{
    [Test]
    public async Task AddJob_PastedDescription_ReturnsJobId()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "alice",
                  pastedTitle: "Senior Engineer",
                  pastedCompany: "Acme",
                  pastedDescription: "Build distributed systems"
                }) {
                  externalId
                  output {
                    jobId
                    title
                    company
                  }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var output = result.GetData("dispatch", "addJob", "output");
        output.GetProperty("jobId").GetString().Should().NotBeNullOrEmpty();
        output.GetProperty("title").GetString().Should().Be("Senior Engineer");
        output.GetProperty("company").GetString().Should().Be("Acme");
    }

    [Test]
    public async Task AddJob_PersistsToDatabase()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "alice",
                  pastedTitle: "Staff Engineer",
                  pastedCompany: "MegaCorp",
                  pastedDescription: "Lead a team"
                }) {
                  output { jobId }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var jobId = Guid.Parse(
            result.GetData("dispatch", "addJob", "output", "jobId").GetString()!
        );

        var job = await JobHuntDb.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
        job.Should().NotBeNull();
        job!.Title.Should().Be("Staff Engineer");
        job.Company.Should().Be("MegaCorp");
        job.UserId.Should().Be("alice");
    }

    [Test]
    public async Task AddJob_UrlOnly_Succeeds()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "alice",
                  url: "https://boards.greenhouse.io/example/jobs/12345"
                }) {
                  output { jobId }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        result
            .GetData("dispatch", "addJob", "output", "jobId")
            .GetString()
            .Should()
            .NotBeNullOrEmpty();
    }

    [Test]
    public async Task AddJob_NoUrlNoPaste_ReturnsGraphQLError()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: { userId: "alice" }) {
                  output { jobId }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeTrue();
    }

    [Test]
    public async Task AddJob_DifferentUsers_AreIsolated()
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

        await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                addJob(input: {
                  userId: "bob",
                  pastedTitle: "Bob Job",
                  pastedCompany: "BobCo",
                  pastedDescription: "Bob work"
                }) { output { jobId } }
              }
            }
            """,
            apiKey: BobKey
        );

        var aliceJobs = await JobHuntDb
            .Jobs.AsNoTracking()
            .Where(j => j.UserId == "alice")
            .ToListAsync();
        var bobJobs = await JobHuntDb
            .Jobs.AsNoTracking()
            .Where(j => j.UserId == "bob")
            .ToListAsync();

        aliceJobs.Should().ContainSingle().Which.Title.Should().Be("Alice Job");
        bobJobs.Should().ContainSingle().Which.Title.Should().Be("Bob Job");
    }
}
