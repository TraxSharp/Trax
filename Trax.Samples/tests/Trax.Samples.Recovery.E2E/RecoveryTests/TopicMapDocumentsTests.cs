using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Api;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The documents the page is built with are the ones the host serves: the topic map's documents in the page's
/// committed client manifest are the ones the running host registers, and a document an earlier version of the page
/// registered by hand (<c>recovery_*</c>) is deactivated, so a client still sending it is refused. The host runs only
/// persisted documents, so a page whose ids drifted from the host's could not start a single draft.
/// </summary>
/// <remarks>
/// After changing the machine (or anything that changes its GraphQL operations), export the manifest and regenerate
/// the client, as the sample's README says.
/// </remarks>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TopicMapDocumentsTests : RecoveryTestFixture
{
    [Test]
    public async Task The_committed_topic_map_documents_are_the_ones_the_host_serves()
    {
        var services = SharedRecoverySetup.Factory.Services;
        var committed = ClientManifest
            .Read(await File.ReadAllTextAsync(TopicMapArtifacts.ClientManifest))
            .Machines.Single(m => m.Id == "topic-map");

        services
            .GetServices<IMachine>()
            .Single(m => m.Name == "topic-map")
            .SchemaHash.Should()
            .Be(
                committed.IrHash,
                "the page is built for the machine as the host declares it now; after changing the machine, "
                    + "export the manifest and regenerate the client"
            );

        // The host registers its documents at startup, keyed by the machine's IR hash. An older version's rows can
        // still be active for a grace period, so only the current version's are compared.
        var prefix = "trax_machine_topic-map_";
        var served = (
            await services
                .GetRequiredService<IPersistedOperationStore>()
                .ListAsync(null, CancellationToken.None)
        )
            .Where(o =>
                o.IsActive
                && o.Id.StartsWith(prefix, StringComparison.Ordinal)
                && o.Id.Contains(committed.IrHash, StringComparison.Ordinal)
            )
            .Select(o => (o.Id, o.Document))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        served.Should().NotBeEmpty("the host exposes the topic map, so it registers its documents");

        committed
            .Documents.Select(d => (d.Id, d.Document))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Should()
            .Equal(
                served,
                "the page is built with the documents the host registers for the machine as it is now "
                    + "(Trax.Docs/adr/0048); after changing the machine, export the manifest and regenerate the client"
            );
    }

    [Test]
    public async Task A_retired_page_document_is_deactivated_and_refused()
    {
        var store =
            SharedRecoverySetup.Factory.Services.GetRequiredService<IPersistedOperationStore>();
        const string id = RetiredPageDocuments.Prefix + "TabletCalls_v1";
        await store.UpsertAsync(
            id,
            "query TabletCalls { discover { orders { tabletCalls { restaurant } } } }",
            new UpsertOptions { Description = "A page document from before the generated client." },
            CancellationToken.None
        );

        await new RetiredPageDocuments(store, NullLogger<RetiredPageDocuments>.Instance).StartAsync(
            CancellationToken.None
        );

        (await store.GetAsync(id, null, CancellationToken.None))
            .Should()
            .BeNull("the page no longer sends it, so the host stops serving it");
        (await store.ListAsync(null, CancellationToken.None))
            .Single(o => o.Id == id)
            .IsActive.Should()
            .BeFalse("it is kept, deactivated, as the record of what was served");
        var sent = await GraphQL.SendIdAsync(id, OperatorKey);
        sent.HasErrors.Should().BeTrue("a deactivated id is refused like an unknown one");
    }
}
