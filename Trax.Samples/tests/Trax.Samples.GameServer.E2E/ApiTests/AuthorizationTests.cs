using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class AuthorizationTests : ApiTestFixture
{
    [Test]
    public async Task BanPlayer_WithPlayerKey_ReturnsAuthorizationError()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    players {
                        banPlayer(
                            input: { playerId: "player-8", reason: "unauthorized attempt" }
                        ) {
                            externalId
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        // BanPlayer requires Admin role — player key should be rejected.
        result.HasErrors.Should().BeTrue();
        result.FirstErrorMessage.Should().NotBeNullOrEmpty();

        // The train should NOT have executed — no metadata should exist.
        DataContext.Reset();
        var metadataCount = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.Name.Contains("BanPlayer"))
            .CountAsync();

        metadataCount
            .Should()
            .Be(0, "authorization rejection should prevent train execution entirely");
    }

    [Test]
    public async Task BanPlayer_WithAdminKey_Succeeds()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    players {
                        banPlayer(
                            input: { playerId: "player-7", reason: "admin ban test" }
                        ) {
                            externalId
                        }
                    }
                }
            }
            """,
            apiKey: AdminKey
        );

        // Admin key has the Admin role — should succeed.
        result.HasErrors.Should().BeFalse($"GraphQL error: {result.FirstErrorMessage}");

        // Verify the train actually executed and persisted metadata.
        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "BanPlayer",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Should().NotBeNull();
    }
}
