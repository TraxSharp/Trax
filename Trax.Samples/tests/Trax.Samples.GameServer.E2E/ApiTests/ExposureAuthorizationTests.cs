using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.ApiTests;

/// <summary>
/// The sample's authorization posture, end to end over HTTP against the running API.
///
/// <para>
/// This host is the shape that motivated both gates: the endpoint is open, because the
/// leaderboard and player lookup are pre-login surfaces and the Banana Cake Pop IDE has to
/// load, while the <c>operations</c> namespace drives the scheduler and must not be. The two
/// requirements cannot both be met by <c>RequireAuthorization()</c>, which is endpoint-wide.
/// </para>
/// </summary>
[TestFixture]
public class ExposureAuthorizationTests : ApiTestFixture
{
    private const string OperationsHealth = "{ operations { health { status } } }";

    /// <summary>
    /// <c>operations.hosts</c> reports instance ids, environment and execution counts. It is the
    /// read surface that used to be reachable with no acknowledgement required at all.
    /// </summary>
    private const string OperationsHosts =
        "{ operations { hosts { instanceId environment totalExecutions } } }";

    private const string PublicLeaderboard = """
        {
            discover {
                players {
                    playerRecords(first: 2) {
                        nodes { displayName wins losses winRate }
                    }
                }
            }
        }
        """;

    private static bool IsAuthorizationError(Utilities.GraphQLResponse result) =>
        result.Root.TryGetProperty("errors", out var errors)
        && errors
            .EnumerateArray()
            .Any(e =>
                e.TryGetProperty("extensions", out var ext)
                && ext.TryGetProperty("code", out var code)
                && code.GetString() == "TRAX_AUTHORIZATION"
            );

    // ── The operations namespace is gated ────────────────────────────────

    [Test]
    public async Task Operations_Anonymous_IsRefused()
    {
        var result = await GraphQL.SendAsync(OperationsHealth);

        IsAuthorizationError(result)
            .Should()
            .BeTrue("GateOperations(roles: \"Admin\") gates the operations field itself");
    }

    [Test]
    public async Task OperationsHosts_Anonymous_IsRefused()
    {
        var result = await GraphQL.SendAsync(OperationsHosts);

        IsAuthorizationError(result)
            .Should()
            .BeTrue(
                "the read surface discloses internal hostnames and workload volume, so it is "
                    + "gated with the mutations rather than left open beside them"
            );
    }

    [Test]
    public async Task Operations_AuthenticatedWithoutTheRole_IsRefused()
    {
        var result = await GraphQL.SendAsync(OperationsHealth, apiKey: PlayerKey);

        IsAuthorizationError(result)
            .Should()
            .BeTrue("a player is authenticated but holds no Admin role");
    }

    [Test]
    public async Task Operations_Admin_IsServed()
    {
        var result = await GraphQL.SendAsync(OperationsHealth, apiKey: AdminKey);

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");
        result
            .GetData("operations", "health")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Healthy");
    }

    // ── The rest of the endpoint stays open ──────────────────────────────

    [Test]
    public async Task PublicQueryModel_Anonymous_IsStillServed()
    {
        var result = await GraphQL.SendAsync(PublicLeaderboard);

        result
            .HasErrors.Should()
            .BeFalse(
                "gating the namespace must leave the pre-login surfaces reachable, which is the "
                    + "posture RequireAuthorization() cannot express"
            );

        result
            .GetData("discover", "players", "playerRecords", "nodes")
            .GetArrayLength()
            .Should()
            .BeGreaterThan(0);
    }

    /// <summary>
    /// <c>winRate</c> is an <c>[ExtendObjectType]</c> field on <c>PlayerRecord</c>, which is
    /// <c>[TraxAllowAnonymous]</c>, so it inherits no gate and carries <c>[AllowAnonymous]</c> of
    /// its own. Before that attribute existed the host refused to start; this asserts the
    /// declared posture is the one actually served.
    /// </summary>
    /// <summary>
    /// The reason <c>winRate</c> declares <c>[Parent(requires:)]</c>. Projection selects only the
    /// columns the caller named, so a query that asks for <c>winRate</c> alone gets a parent whose
    /// Wins and Losses were never fetched. Without the declaration they arrive as 0 and the field
    /// silently returns 0 rather than failing.
    /// </summary>
    [Test]
    public async Task ExtensionField_SelectedAlone_StillReadsItsParentColumns()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(first: 5) {
                            nodes { winRate }
                        }
                    }
                }
            }
            """
        );

        result.HasErrors.Should().BeFalse($"GraphQL error: {result.FirstErrorMessage}");

        var rates = result
            .GetData("discover", "players", "playerRecords", "nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("winRate").GetDouble())
            .ToList();

        rates.Should().NotBeEmpty();
        rates
            .Should()
            .Contain(r => r > 0, "a parent whose Wins and Losses were not projected returns 0");
    }

    [Test]
    public async Task AnonymousDeclaredExtensionField_IsServedToAnAnonymousCaller()
    {
        var result = await GraphQL.SendAsync(PublicLeaderboard);

        result.HasErrors.Should().BeFalse();

        var first = result
            .GetData("discover", "players", "playerRecords", "nodes")
            .EnumerateArray()
            .First();

        var wins = first.GetProperty("wins").GetInt32();
        var losses = first.GetProperty("losses").GetInt32();
        var winRate = first.GetProperty("winRate").GetDouble();

        winRate
            .Should()
            .BeApproximately(
                wins + losses > 0 ? (double)wins / (wins + losses) : 0,
                1e-9,
                "the extension field resolves off the parent it was grafted onto"
            );
    }
}
