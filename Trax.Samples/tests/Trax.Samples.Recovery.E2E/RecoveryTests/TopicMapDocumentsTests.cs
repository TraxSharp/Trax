using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Samples.Recovery.Api;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The documents the page is built with are the ones the host serves: the topic map's committed
/// <c>topic-map.documents.json</c> is the host's manifest for the machine, and every one of the page's own
/// documents is registered. The host runs only persisted documents, so a page whose ids drifted from the host's
/// could not start a single draft.
/// </summary>
/// <remarks>
/// After changing the machine (or anything that changes its GraphQL operations), write the file again from the host
/// this suite starts: <c>TRAX_UPDATE_MACHINE_DOCUMENTS=1 dotnet test --filter TopicMapDocumentsTests</c>, from the
/// test project. The twin's IR hash and the documents' must agree, so regenerate the twin first.
/// </remarks>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TopicMapDocumentsTests : RecoveryTestFixture
{
    private const string Manifest =
        "{ operations { machineDocuments { id machine operation irHash document } } }";

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Test]
    public async Task The_committed_topic_map_documents_are_the_ones_the_host_serves()
    {
        var response = await GraphQL.SendAsync(Manifest, OperatorKey);
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var served = response
            .Data.GetProperty("operations")
            .GetProperty("machineDocuments")
            .EnumerateArray()
            .Select(d => d.Deserialize<ManifestEntry>(JsonSerializerOptions.Web)!)
            .Where(d => d.Machine == "topic-map")
            .ToList();
        served.Should().NotBeEmpty("the host exposes the topic map, so it serves its documents");

        if (Environment.GetEnvironmentVariable("TRAX_UPDATE_MACHINE_DOCUMENTS") == "1")
            await File.WriteAllTextAsync(
                TopicMapArtifacts.Documents,
                JsonSerializer.Serialize(served, Written).Replace("\r\n", "\n") + "\n"
            );

        File.Exists(TopicMapArtifacts.Documents)
            .Should()
            .BeTrue(
                $"the page sends the topic map's operations by the ids in {TopicMapArtifacts.Documents}; write it "
                    + "with TRAX_UPDATE_MACHINE_DOCUMENTS=1"
            );
        var committed = JsonSerializer.Deserialize<List<ManifestEntry>>(
            await File.ReadAllTextAsync(TopicMapArtifacts.Documents),
            JsonSerializerOptions.Web
        )!;
        committed
            .Should()
            .Equal(
                served,
                "the page is built with the documents the host registers for the machine as it is now "
                    + "(Trax.Docs/adr/0048); after changing the machine, regenerate the twin and then this file "
                    + "with TRAX_UPDATE_MACHINE_DOCUMENTS=1"
            );
        var twin = await File.ReadAllTextAsync(
            Path.Combine(TopicMapArtifacts.Folder, "topic-map.machine.g.ts")
        );
        committed
            .Should()
            .OnlyContain(
                d => twin.Contains($"irHash = \"{d.IrHash}\"", StringComparison.Ordinal),
                "the documents are of the IR the page's twin was generated from"
            );
    }

    [Test]
    public async Task Every_page_document_is_registered_as_the_page_sends_it()
    {
        var store =
            SharedRecoverySetup.Factory.Services.GetRequiredService<IPersistedOperationStore>();
        var page = PageDocuments.Read();
        page.Should().NotBeEmpty();

        foreach (var document in page)
        {
            var stored = await store.GetAsync(document.Id, null, CancellationToken.None);
            stored
                .Should()
                .NotBeNull(
                    $"the page sends {document.Name} as {document.Id}, which the host must hold"
                );
            stored!.Document.Should().Be(document.Document);
        }
        page.Select(d => d.Id).Should().OnlyHaveUniqueItems();
    }

    /// <summary>One entry of the manifest, in the order the committed file writes its members.</summary>
    private sealed record ManifestEntry(
        string Id,
        string Machine,
        string Operation,
        string IrHash,
        string Document
    );
}
