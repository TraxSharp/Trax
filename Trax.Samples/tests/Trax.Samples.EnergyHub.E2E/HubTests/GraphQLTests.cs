using System.Text.Json;
using Trax.Effect.Enums;
using Trax.Samples.EnergyHub.E2E.Fixtures;
using Trax.Samples.EnergyHub.E2E.Utilities;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[TestFixture]
public class GraphQLTests : HubTestFixture
{
    [Test]
    public async Task MonitorSolarProduction_query_is_answered_anonymously()
    {
        var result = await GetGraphQLClient()
            .SendAsync(
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
    public async Task Queued_report_from_an_operator_completes_on_the_worker()
    {
        var result = await GetGraphQLClient()
            .SendAsync(
                """
                mutation {
                    dispatch {
                        sustainability {
                            generateSustainabilityReport(input: { reportPeriod: "Daily" }) {
                                externalId
                                workQueueId
                            }
                        }
                    }
                }
                """,
                apiKey: OperatorKey
            );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var queued = result.GetData("dispatch", "sustainability", "generateSustainabilityReport");
        queued.GetProperty("workQueueId").GetInt64().Should().BeGreaterThan(0);

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "GenerateSustainabilityReport",
            TrainState.Completed
        );
        metadata.Output.Should().Contain("Daily");
    }

    [Test]
    public async Task Anonymous_caller_cannot_queue_a_trade()
    {
        var result = await GetGraphQLClient()
            .SendAsync(
                """
                mutation {
                    dispatch {
                        tradeGridEnergy(input: { ratePerKwh: 0.14, maxSellPercent: 80 }) {
                            externalId
                        }
                    }
                }
                """
            );

        result.HasErrors.Should().BeTrue("a trade moves money, so only an operator may queue one");
        result.FirstErrorMessage.Should().Be("Not authorized.");
    }

    [Test]
    public async Task Operator_can_list_the_trains()
    {
        var result = await GetGraphQLClient()
            .SendAsync(
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
                """,
                apiKey: OperatorKey
            );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var trains = result.GetData("operations", "trains");
        trains
            .EnumerateArray()
            .Any(t =>
                t.GetProperty("serviceTypeName").GetString()?.Contains("MonitorSolarProduction")
                    == true
                && t.GetProperty("isQuery").GetBoolean()
            )
            .Should()
            .BeTrue("MonitorSolarProduction should be registered as a query");
    }

    [Test]
    public async Task Operator_can_read_health()
    {
        var result = await GetGraphQLClient()
            .SendAsync("{ operations { health { status } } }", apiKey: OperatorKey);

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        result
            .GetData("operations", "health")
            .GetProperty("status")
            .ValueKind.Should()
            .Be(JsonValueKind.String);
    }
}
