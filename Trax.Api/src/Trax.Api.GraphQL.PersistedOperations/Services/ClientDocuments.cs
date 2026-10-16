using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Utilities;
using Trax.Api.GraphQL.Validation;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The documents a host's frontend wrote, read from the <c>trax.documents.json</c> that <c>trax client --documents</c>
/// writes, checked and registered at startup beside the generated ones, each under an id in its own reserved prefix
/// that carries the hash of its text.
/// </summary>
/// <remarks>
/// <para>
/// The id is <c>trax_client_&lt;name&gt;_&lt;textHash&gt;&lt;check&gt;</c>: the operation's name (a <c>_</c> written
/// <c>-</c>, as a train's), the SHA-256 of the text, and 16 hex characters of a hash of that and the text, so a stored
/// row is a client document only when its body is the one its id names.
/// </para>
/// <para>
/// A client document reaches trains only: one named <c>query</c> or <c>mutation</c>, whose root selects
/// <c>dispatch</c> or <c>discover</c>, under it only trains and the namespaces holding them, a mutation exactly one
/// train, and no directive anywhere. Everything else (the operations surface, persisted-operation management,
/// machines, <c>runProgress</c>, introspection, a subscription) refuses the host's start, as does a document the
/// host's own validation refuses. The decision is
/// <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.
/// </para>
/// </remarks>
internal static class ClientDocuments
{
    /// <summary>The id prefix client documents are stored under, and no uploaded document may use.</summary>
    public const string ReservedIdPrefix = "trax_client_";

    /// <summary>The format of <c>trax.documents.json</c> this version reads.</summary>
    public const int Format = 1;

    private const string MutationRootField = "dispatch";
    private const string QueryRootField = "discover";

    /// <summary>Whether <paramref name="id"/> is under the reserved prefix.</summary>
    public static bool IsReserved(string? id) =>
        id is not null && id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal);

    /// <summary>The id of the client document <paramref name="name"/> with <paramref name="text"/>.</summary>
    public static string IdOf(string name, string text)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return $"{SlotOf(name)}_{hash}{TrainDocuments.TextHash(hash, text)}";
    }

    /// <summary>The slot every version of the client document <paramref name="name"/> shares.</summary>
    public static string SlotOf(string name) => ReservedIdPrefix + name.Replace('_', '-');

    /// <summary>
    /// The slot a client id names, or null when the id is not one: the prefix, a non-empty name and a well-formed hash.
    /// </summary>
    public static string? SlotOfId(string id)
    {
        if (!IsReserved(id) || !MachineDocuments.IsWellFormed(id))
            return null;
        var name = id[ReservedIdPrefix.Length..id.LastIndexOf('_')];
        return name.Length == 0 || name.Contains('_') ? null : id[..id.LastIndexOf('_')];
    }

    /// <summary>
    /// The documents in the file at <paramref name="path"/>, in name order.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file is missing or unreadable, is not the format this version reads, or names a document twice or with a
    /// name its text does not hold.
    /// </exception>
    public static IReadOnlyList<ClientDocument> Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The client documents file '{path}' cannot be read ({ex.Message}). Run trax client --documents, "
                    + "and name the file it writes in ClientDocuments(...), relative to the content root.",
                ex
            );
        }

        ClientDocumentsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ClientDocumentsFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The client documents file '{path}' is not JSON trax client wrote ({ex.Message}). Write it again with "
                    + "trax client --documents.",
                ex
            );
        }
        if (file is null || file.Format != Format || file.Documents is null)
            throw new InvalidOperationException(
                $"The client documents file '{path}' is not format {Format}, the format this host reads. Write it "
                    + "again with the trax client of this Trax version."
            );

        var documents = new List<ClientDocument>();
        foreach (var entry in file.Documents)
        {
            if (
                entry is null
                || string.IsNullOrEmpty(entry.Name)
                || string.IsNullOrEmpty(entry.Text)
                || !NameUtils.IsValidGraphQLName(entry.Name)
            )
                throw new InvalidOperationException(
                    $"The client documents file '{path}' holds a document without a GraphQL name or a text. Write it "
                        + "again with trax client --documents."
                );
            var owner =
                $"client document '{entry.Name}'"
                + (string.IsNullOrEmpty(entry.Source) ? "" : $" ({entry.Source})");
            if (documents.Any(d => d.Name == entry.Name))
                throw new InvalidOperationException(
                    $"The client documents file '{path}' names the {owner} twice. Operation names are unique."
                );
            documents.Add(
                new ClientDocument(IdOf(entry.Name, entry.Text), entry.Text, entry.Name, owner)
            );
        }
        return documents.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Refuses a document that is not one named operation reaching trains only, or whose name is not the one it is
    /// registered under, naming the document and why.
    /// </summary>
    /// <exception cref="InvalidOperationException">A document breaks a rule.</exception>
    public static void Check(ISchemaDefinition schema, IEnumerable<ClientDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(documents);

        var refusals = new List<string>();
        foreach (var document in documents)
            if (Refusal(schema, document.Name, document.Text) is { } reason)
                refusals.Add($"The {document.Owner} {reason}");
        if (refusals.Count > 0)
            throw new InvalidOperationException(
                string.Join("\n", refusals)
                    + "\nA client document holds one named query or mutation of trains under dispatch or discover."
            );
    }

    /// <summary>Why <paramref name="text"/> may not be a client document, or null when it may.</summary>
    internal static string? Refusal(ISchemaDefinition schema, string name, string text)
    {
        DocumentNode parsed;
        try
        {
            parsed = Utf8GraphQLParser.Parse(text);
        }
        catch (SyntaxException ex)
        {
            return $"is not GraphQL ({ex.Message}).";
        }

        var operations = parsed.Definitions.OfType<OperationDefinitionNode>().ToList();
        if (operations.Count != 1)
            return $"holds {operations.Count} operations; it holds one.";
        if (
            parsed.Definitions.Any(d =>
                d is not (OperationDefinitionNode or FragmentDefinitionNode)
            )
        )
            return "holds a definition that is neither an operation nor a fragment.";
        var operation = operations[0];
        if (operation.Name?.Value != name)
            return $"is registered as '{name}' but its operation is named '{operation.Name?.Value}'.";
        if (operation.Operation is not (OperationType.Query or OperationType.Mutation))
            return $"is a {operation.Operation.ToString().ToLowerInvariant()}; a client document is a query or a mutation.";
        if (HasDirective(parsed))
            return "uses a directive; a client document uses none.";

        var mutation = operation.Operation == OperationType.Mutation;
        var rootName = mutation ? MutationRootField : QueryRootField;
        var root = mutation ? schema.MutationType : schema.QueryType;
        if (
            root is null
            || !root.Fields.TryGetField(rootName, out var rootField)
            || rootField.Type.NamedType() is not IObjectTypeDefinition holder
        )
            return $"reaches '{rootName}', which this host does not serve.";

        if (
            operation.SelectionSet.Selections is not [FieldNode { Alias: null } top]
            || top.Name.Value != rootName
        )
            return $"selects something other than '{rootName}' at its root.";

        var trains = 0;
        foreach (var selection in top.SelectionSet?.Selections ?? [])
        {
            if (selection is not FieldNode field)
                return $"selects a fragment directly under '{rootName}'; select the trains themselves.";
            if (
                field.Name.Value.StartsWith("__", StringComparison.Ordinal)
                || !holder.Fields.TryGetField(field.Name.Value, out var definition)
            )
                return $"selects '{rootName}.{field.Name.Value}', which this host does not serve.";
            if (!NamespaceField.IsNamespace(definition))
            {
                trains++;
                continue;
            }
            if (field.Alias is not null)
                return $"aliases the namespace '{field.Name.Value}'; alias a train, not its namespace.";
            if (definition.Type.NamedType() is not IObjectTypeDefinition ns)
                return $"selects the namespace '{field.Name.Value}', which holds no trains.";
            foreach (var inner in field.SelectionSet?.Selections ?? [])
            {
                if (
                    inner is not FieldNode train
                    || train.Name.Value.StartsWith("__", StringComparison.Ordinal)
                    || !ns.Fields.TryGetField(train.Name.Value, out var trainDefinition)
                    || NamespaceField.IsNamespace(trainDefinition)
                )
                    return $"selects something other than a train under the namespace '{field.Name.Value}'.";
                trains++;
            }
        }
        if (trains == 0)
            return "selects no train.";
        if (mutation && trains != 1)
            return $"runs {trains} trains in one mutation; a mutation runs one.";
        return null;
    }

    private static bool HasDirective(ISyntaxNode node) =>
        node is DirectiveNode || node.GetNodes().Any(HasDirective);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record ClientDocumentsFile
    {
        public int Format { get; init; }
        public List<ClientDocumentsEntry?>? Documents { get; init; }
    }

    private sealed record ClientDocumentsEntry
    {
        public string? Name { get; init; }
        public string? Source { get; init; }
        public string? Text { get; init; }
    }
}

/// <summary>One client document, as <see cref="ClientDocuments.Load"/> reads it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Text">The GraphQL document text, exactly as the file holds it.</param>
/// <param name="Name">The operation's name.</param>
/// <param name="OwnerText">What it is, for a refusal or a log line (<c>client document 'OrderCard' (src/...)</c>).</param>
internal sealed record ClientDocument(string Id, string Text, string Name, string OwnerText)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ClientDocuments.SlotOf(Name);

    /// <inheritdoc />
    public override string Owner => OwnerText;

    /// <inheritdoc />
    public override string Description => $"Written by the frontend: the {OwnerText}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(ClientDocuments.IdOf(Name, stored), Id, StringComparison.Ordinal);
}
