using System.Text.Json;
using Trax.Samples.EnergyHub.E2E.Fixtures;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[TestFixture]
public class GraphQLTests : HubTestFixture
{
    [Test]
    public async Task MonitorSolarProduction_Query()
    {
        using var client = GetHttpClient();
        var graphql = new Utilities.GraphQLClient(client);

        var result = await graphql.SendAsync(
            """
            {
                discover {
                    solar {
                        monitorSolarProduction(
                            input: { arrayId: "SPA-001", region: "somerset" }
                        ) {
                            arrayId
                            totalKwh
                            efficiency
                        }
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var output = result.GetData("discover", "solar", "monitorSolarProduction");
        output.GetProperty("arrayId").GetString().Should().Be("SPA-001");
        output.GetProperty("totalKwh").GetDouble().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task GenerateSustainabilityReport_RunMutation()
    {
        using var client = GetHttpClient();
        var graphql = new Utilities.GraphQLClient(client);

        var result = await graphql.SendAsync(
            """
            mutation {
                dispatch {
                    sustainability {
                        generateSustainabilityReport(
                            input: { reportPeriod: "Daily" }
                            mode: RUN
                        ) {
                            externalId
                            output {
                                reportPeriod
                                carbonOffsetTons
                                renewablePercent
                            }
                        }
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var output = result
            .GetData("dispatch", "sustainability", "generateSustainabilityReport")
            .GetProperty("output");

        output.GetProperty("reportPeriod").GetString().Should().Be("Daily");
    }

    [Test]
    public async Task Operations_GetTrains()
    {
        using var client = GetHttpClient();
        var graphql = new Utilities.GraphQLClient(client);

        var result = await graphql.SendAsync(
            """
            {
                operations {
                    trains {
                        serviceTypeName
                        isQuery
                        isMutation
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var trains = result.GetData("operations", "trains");
        trains.GetArrayLength().Should().BeGreaterThan(0);

        // MonitorSolarProduction should be a query
        var solar = trains
            .EnumerateArray()
            .Any(t =>
                t.GetProperty("serviceTypeName").GetString()?.Contains("MonitorSolarProduction")
                    == true
                && t.GetProperty("isQuery").GetBoolean()
            );

        solar.Should().BeTrue("MonitorSolarProduction should be registered as a query");
    }

    [Test]
    public async Task Operations_GetHealth()
    {
        using var client = GetHttpClient();
        var graphql = new Utilities.GraphQLClient(client);

        var result = await graphql.SendAsync(
            """
            {
                operations {
                    health {
                        status
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        result.GetData("operations", "health").TryGetProperty("status", out _).Should().BeTrue();
    }
}
