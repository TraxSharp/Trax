using System.Text.Json;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Samples.Recovery.Api;

/// <summary>
/// Registers the page's own GraphQL documents (the runs it starts and reads, outside the topic map) as persisted
/// operations when the host starts, from <c>persisted-operations.json</c> in the page's source, which the page
/// sends each by its id. The host runs only persisted documents; the topic map's are generated and registered by
/// Trax itself. A document already stored as it is here is left alone.
/// </summary>
public sealed class PageDocuments(IPersistedOperationStore store, ILogger<PageDocuments> logger)
    : IHostedService
{
    private const string Resource = "Trax.Samples.Recovery.Api.persisted-operations.json";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var operation in Read())
        {
            var stored = await store.GetAsync(operation.Id, tenantKey: null, cancellationToken);
            if (stored?.Document == operation.Document)
                continue;
            await store.UpsertAsync(
                operation.Id,
                operation.Document,
                new UpsertOptions { Description = $"The Recovery page's {operation.Name}." },
                cancellationToken
            );
            logger.LogInformation(
                "Registered the page's {Operation} as {Id}.",
                operation.Name,
                operation.Id
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The page's documents, as persisted-operations.json lists them.</summary>
    public static IReadOnlyList<PageDocument> Read()
    {
        using var stream =
            typeof(PageDocuments).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The resource {Resource} is not embedded.");
        return JsonSerializer
                .Deserialize<PageDocumentFile>(stream, JsonSerializerOptions.Web)
                ?.Operations
            ?? throw new InvalidOperationException($"{Resource} lists no operations.");
    }

    private sealed record PageDocumentFile(IReadOnlyList<PageDocument> Operations);
}

/// <summary>One of the page's persisted documents.</summary>
/// <param name="Name">The operation's name in the document.</param>
/// <param name="Id">The id the page sends it by.</param>
/// <param name="Document">The document.</param>
public sealed record PageDocument(string Name, string Id, string Document);
