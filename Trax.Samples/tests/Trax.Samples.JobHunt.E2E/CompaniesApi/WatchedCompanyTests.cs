using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E.CompaniesApi;

[TestFixture]
public class WatchedCompanyTests : JobHuntApiTestFixture
{
    [Test]
    public async Task WatchCompany_ReturnsId()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                watchCompany(input: {
                  userId: "alice",
                  companyName: "Acme",
                  careersUrl: "https://acme.com/careers"
                }) {
                  output { watchedCompanyId companyName }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var output = result.GetData("dispatch", "watchCompany", "output");
        output.GetProperty("watchedCompanyId").GetString().Should().NotBeNullOrEmpty();
        output.GetProperty("companyName").GetString().Should().Be("Acme");
    }

    [Test]
    public async Task ListWatchedCompanies_AfterWatch_ReturnsCompany()
    {
        await GraphQL.SendAsync(
            """
            mutation {
              dispatch {
                watchCompany(input: {
                  userId: "alice",
                  companyName: "MegaCorp",
                  careersUrl: "https://megacorp.com/jobs"
                }) { output { watchedCompanyId } }
              }
            }
            """,
            apiKey: AliceKey
        );

        var result = await GraphQL.SendAsync(
            """
            {
              discover {
                listWatchedCompanies(input: { userId: "alice" }) {
                  companies { id companyName careersUrl }
                }
              }
            }
            """,
            apiKey: AliceKey
        );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        var companies = result.GetData("discover", "listWatchedCompanies", "companies");
        companies.GetArrayLength().Should().Be(1);
        companies[0].GetProperty("companyName").GetString().Should().Be("MegaCorp");
    }

    [Test]
    public async Task Startup_CreatesMonitorAllWatchedCompaniesManifest()
    {
        var manifests = await DataContext
            .Manifests.AsNoTracking()
            .Select(m => m.ExternalId)
            .ToListAsync();

        manifests.Should().Contain("MonitorAllWatchedCompanies");
    }
}
