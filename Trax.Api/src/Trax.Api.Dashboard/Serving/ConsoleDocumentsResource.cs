using System.Text.Json;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.Dashboard.Serving;

/// <summary>
/// The documents the app sends, embedded from <c>ClientApp/console-documents.json</c>, which the app's
/// <c>npm run documents</c> writes from its source. <c>UseTraxApiDashboard</c> hands them to the GraphQL host,
/// which registers them as persisted documents when it runs persisted operations.
/// </summary>
internal static class ConsoleDocumentsResource
{
    /// <summary>The resource's name in this assembly.</summary>
    public const string Name = "TraxApiDashboardDocuments/console-documents.json";

    private const int Format = 1;

    private static readonly Lazy<IReadOnlyList<ConsoleDocumentText>> Embedded = new(Read);

    /// <summary>The documents this build of the app sends.</summary>
    public static IReadOnlyList<ConsoleDocumentText> Documents => Embedded.Value;

    private static IReadOnlyList<ConsoleDocumentText> Read()
    {
        using var stream =
            typeof(ConsoleDocumentsResource).Assembly.GetManifestResourceStream(Name)
            ?? throw new InvalidOperationException(
                $"This build of Trax.Api.Dashboard carries no '{Name}'. The package from nuget.org always does."
            );
        var file =
            JsonSerializer.Deserialize<DocumentsFile>(stream, Json)
            ?? throw new InvalidOperationException($"'{Name}' is empty.");
        if (file.Format != Format || file.Documents is null)
            throw new InvalidOperationException(
                $"'{Name}' is not format {Format}. Write it again with `npm run documents` in ClientApp."
            );
        return [.. file.Documents.Select(d => new ConsoleDocumentText(d.Name, d.Text))];
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record DocumentsFile(int Format, List<DocumentEntry>? Documents);

    private sealed record DocumentEntry(string Name, string Text);
}
