using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Services;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The persisted documents a host registers for its exposed machines, read from <c>trax.persisted_operation</c>, and
/// requests that name one by its id. The wire contract these tests fix:
/// <list type="bullet">
/// <item>Each exposed machine's operations are stored under <see cref="MachineDocuments.ReservedIdPrefix"/>, as
/// <c>trax_machine_&lt;machine&gt;_&lt;operation&gt;_&lt;hash&gt;</c>, under the null tenant. A persisted id may not
/// contain a dot (HotChocolate refuses it as malformed), and a machine id is kebab-case and an operation camelCase,
/// so <c>_</c> separates them unambiguously.</item>
/// <item>A document takes its values as variables: <c>input</c> and <c>requestId</c> for <c>start</c>; <c>id</c>,
/// <c>input</c> (when the event has one) and <c>requestId</c> for an event and <c>discard</c>; <c>id</c> for
/// <c>get</c> and <c>machineChanged</c>.</item>
/// <item>The answer has the shape of the inline operation: <c>data.machines.&lt;machine&gt;.&lt;operation&gt;</c>.</item>
/// </list>
/// </summary>
public static class MachineDocumentOps
{
    /// <summary>The operations of an exposed wizard whose documents the host registers.</summary>
    public static readonly IReadOnlyList<string> WizardOperations =
    [
        "start",
        "discard",
        "get",
        .. Fakes.WizardMachine.ClientEventFields,
    ];

    /// <summary>The subscription's operation, registered with the rest.</summary>
    public const string MachineChanged = "machineChanged";

    /// <summary>
    /// Persisted operations over the host's data context, on one node's cache, with inline documents still served
    /// (logged), so a test can drive the inline operations and the persisted ones side by side.
    /// <paramref name="operationsNamespace"/> exposes the management operations, which needs the operations gate
    /// (<see cref="E2EHostOptions.ExposeOperations"/>).
    /// </summary>
    public static Func<TraxGraphQLBuilder, TraxGraphQLBuilder> Persisted(
        bool operationsNamespace = false,
        bool requirePersisted = false
    ) =>
        graphql =>
            graphql.UsePersistedOperations(po =>
                po.SingleNode()
                    .RequirePersisted(requirePersisted)
                    .LogNonPersistedRequests(!requirePersisted)
                    .ExposeOperationsNamespace(operationsNamespace)
            );

    /// <summary>One stored persisted document.</summary>
    public sealed record StoredDocument(string TenantKey, string Id, string Document, bool IsActive)
    {
        /// <summary>The operation the id names: the segment between the machine and the hash.</summary>
        public string? Operation(string machine)
        {
            var prefix = MachineDocuments.ReservedIdPrefix + machine + "_";
            if (!Id.StartsWith(prefix, StringComparison.Ordinal))
                return null;
            var rest = Id[prefix.Length..];
            var hash = rest.LastIndexOf('_');
            return hash < 0 ? null : rest[..hash];
        }

        /// <summary>The hash at the end of the id, after the last <c>_</c>.</summary>
        public string Hash
        {
            get
            {
                var at = Id.LastIndexOf('_');
                return at < 0 ? "" : Id[(at + 1)..];
            }
        }
    }

    /// <summary>Every stored document under the reserved prefix, every tenant, active or not, by id.</summary>
    public static async Task<IReadOnlyList<StoredDocument>> MachineDocumentsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(E2EHost.ConnectionString(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT tenant_key, id, document, is_active FROM trax.persisted_operation "
                + "WHERE starts_with(id, @prefix) ORDER BY id, tenant_key",
            connection
        );
        command.Parameters.AddWithValue("prefix", MachineDocuments.ReservedIdPrefix);
        var rows = new List<StoredDocument>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(
                new StoredDocument(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3)
                )
            );
        return rows;
    }

    /// <summary>The documents of <paramref name="machine"/> under the null tenant.</summary>
    public static async Task<IReadOnlyList<StoredDocument>> MachineDocumentsAsync(
        string database,
        string machine
    ) =>
        (await MachineDocumentsAsync(database))
            .Where(d => d.TenantKey == "" && d.Operation(machine) is not null)
            .ToList();

    /// <summary>
    /// The id of the active null-tenant document of <paramref name="operation"/> of <paramref name="machine"/>.
    /// Fails the test when there is none, or more than one (the caller then names the hash it wants).
    /// </summary>
    public static async Task<string> IdOfAsync(string database, string machine, string operation)
    {
        var matches = (await MachineDocumentsAsync(database, machine))
            .Where(d => d.IsActive && d.Operation(machine) == operation)
            .ToList();
        if (matches.Count != 1)
            Assert.Fail(
                $"Expected one stored document for {machine}.{operation} under {MachineDocuments.ReservedIdPrefix}, "
                    + $"found {matches.Count}: [{string.Join(", ", matches.Select(m => m.Id))}]"
            );
        return matches[0].Id;
    }

    /// <summary>How many history rows the documents under the reserved prefix have.</summary>
    public static Task<long> HistoryRowsAsync(string database) =>
        E2ESql.ScalarAsync<long>(
            database,
            "SELECT count(*) FROM trax.persisted_operation_history WHERE starts_with(id, @prefix)",
            ("prefix", MachineDocuments.ReservedIdPrefix)
        );

    /// <summary>
    /// POSTs a request naming the persisted document <paramref name="documentId"/> with
    /// <paramref name="variables"/>, as <paramref name="apiKey"/>'s caller.
    /// </summary>
    public static async Task<JsonDocument> PostPersistedAsync(
        this IHost host,
        string documentId,
        object? variables = null,
        string? apiKey = null
    ) => (await host.PostPersistedWithStatusAsync(documentId, variables, apiKey)).Body;

    /// <summary>As <see cref="PostPersistedAsync"/>, with the HTTP status of the answer.</summary>
    public static async Task<(int Status, JsonDocument Body)> PostPersistedWithStatusAsync(
        this IHost host,
        string documentId,
        object? variables = null,
        string? apiKey = null
    )
    {
        var client = host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = variables is null
                ? JsonContent.Create(new { id = documentId })
                : JsonContent.Create(new { id = documentId, variables }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        return (
            (int)response.StatusCode,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync())
        );
    }

    /// <summary>
    /// Every code the answer carries: each error's <c>extensions.code</c>, and each <c>problem.code</c> anywhere in
    /// its data. A refusal of a machine document is one of them, however it is carried.
    /// </summary>
    public static IReadOnlyList<string> Codes(JsonDocument doc)
    {
        var codes = new List<string>();
        if (
            doc.RootElement.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
        )
            foreach (var error in errors.EnumerateArray())
                if (
                    error.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                    && code.GetString() is { } text
                )
                    codes.Add(text);
        if (doc.RootElement.TryGetProperty("data", out var data))
            CollectProblems(data, codes);
        return codes;
    }

    /// <summary>Whether the answer carries data for <c>machines.&lt;machine&gt;.&lt;operation&gt;</c> and no error.</summary>
    public static bool Succeeded(JsonDocument doc, string machine, string operation) =>
        !(
            doc.RootElement.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0
        )
        && doc.RootElement.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("machines", out var machines)
        && machines.ValueKind == JsonValueKind.Object
        && machines.TryGetProperty(machine, out var m)
        && m.ValueKind == JsonValueKind.Object
        && m.TryGetProperty(operation, out var op)
        && op.ValueKind == JsonValueKind.Object
        && !(
            op.TryGetProperty("problem", out var problem)
            && problem.ValueKind == JsonValueKind.Object
        );

    /// <summary>The first 600 characters of the answer, for a failure message.</summary>
    public static string Show(JsonDocument doc)
    {
        var text = doc.RootElement.GetRawText();
        return text.Length <= 600 ? text : text[..600];
    }

    private static void CollectProblems(JsonElement element, List<string> codes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (
                        property.Name == "problem"
                        && property.Value.ValueKind == JsonValueKind.Object
                        && property.Value.TryGetProperty("code", out var code)
                        && code.GetString() is { } text
                    )
                        codes.Add(text);
                    else
                        CollectProblems(property.Value, codes);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectProblems(item, codes);
                break;
        }
    }
}
