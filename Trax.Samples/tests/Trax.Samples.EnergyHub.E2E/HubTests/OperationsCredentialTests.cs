using System.Text.Json;
using Trax.Samples.EnergyHub.E2E.Fixtures;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

/// <summary>
/// The hub's <c>operations</c> namespace triggers, disables and cancels manifests and changes
/// scheduler settings, and its reads list every manifest and execution, so a caller with no
/// credential must not reach it. The operator key does.
/// </summary>
[TestFixture]
public class OperationsCredentialTests : HubTestFixture
{
    private const string ManifestsQuery =
        "{ operations { manifests(take: 5) { items { externalId } } } }";

    [Test]
    public async Task Anonymous_caller_cannot_read_the_scheduler_control_plane()
    {
        var result = await GetGraphQLClient().SendAsync(ManifestsQuery);

        ReturnedManifests(result.Root)
            .Should()
            .BeFalse("an unauthenticated caller must be refused the operations namespace");
        result.HasErrors.Should().BeTrue();
    }

    [Test]
    public async Task Anonymous_caller_cannot_trigger_a_manifest()
    {
        var result = await GetGraphQLClient()
            .SendAsync(
                $$"""mutation { operations { triggerManifest(externalId: "{{ManifestNames.TradeGridEnergy}}") { success } } }"""
            );

        result.FirstErrorMessage.Should().Be("Not authorized.");
    }

    [Test]
    public async Task Operator_reads_the_scheduler_control_plane()
    {
        var result = await GetGraphQLClient().SendAsync(ManifestsQuery, apiKey: OperatorKey);

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        ReturnedManifests(result.Root).Should().BeTrue();
    }

    private static bool ReturnedManifests(JsonElement root) =>
        root.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("operations", out var operations)
        && operations.ValueKind == JsonValueKind.Object
        && operations.GetProperty("manifests").GetProperty("items").GetArrayLength() > 0;
}
