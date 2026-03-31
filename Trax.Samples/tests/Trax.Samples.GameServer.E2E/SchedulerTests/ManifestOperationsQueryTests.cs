using Trax.Samples.GameServer.E2E.Factories;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests for operations manifest queries via the API's GraphQL endpoint.
/// Lives in SchedulerTests because manifests are seeded by SharedSchedulerSetup.
/// Creates its own API factory for the GraphQL client since the scheduler has no
/// GraphQL endpoint and SharedApiSetup only initializes for tests in ApiTests namespace.
/// </summary>
[TestFixture]
public class ManifestOperationsQueryTests : SchedulerTestFixture
{
    private GameServerApiFactory ApiFactory { get; set; } = null!;
    private HttpClient ApiHttpClient { get; set; } = null!;
    private GraphQLClient GraphQL { get; set; } = null!;

    [OneTimeSetUp]
    public void SetUpGraphQL()
    {
        ApiFactory = new GameServerApiFactory();
        ApiHttpClient = ApiFactory.CreateClient();
        GraphQL = new GraphQLClient(ApiHttpClient);
    }

    [OneTimeTearDown]
    public async Task TearDownGraphQL()
    {
        ApiHttpClient.Dispose();
        await ApiFactory.DisposeAsync();
    }

    [Test]
    public async Task GetManifests_ReturnsPaginatedList()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                operations {
                    manifests(take: 5) {
                        items {
                            id
                            externalId
                            name
                            isEnabled
                            scheduleType
                        }
                        totalCount
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var manifests = result.GetData("operations", "manifests");
        manifests.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        manifests.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task GetManifest_ById_ReturnsSingle()
    {
        var listResult = await GraphQL.SendAsync(
            """
            {
                operations {
                    manifests(take: 1) {
                        items {
                            id
                            externalId
                        }
                    }
                }
            }
            """
        );

        listResult.HasErrors.Should().BeFalse();
        var firstManifest = listResult
            .GetData("operations", "manifests", "items")
            .EnumerateArray()
            .First();
        var manifestId = firstManifest.GetProperty("id").GetInt64();
        var expectedExternalId = firstManifest.GetProperty("externalId").GetString();

        var result = await GraphQL.SendAsync(
            $$"""
            {
                operations {
                    manifest(id: {{manifestId}}) {
                        id
                        externalId
                        name
                        isEnabled
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var manifest = result.GetData("operations", "manifest");
        manifest.GetProperty("id").GetInt64().Should().Be(manifestId);
        manifest.GetProperty("externalId").GetString().Should().Be(expectedExternalId);
    }
}
