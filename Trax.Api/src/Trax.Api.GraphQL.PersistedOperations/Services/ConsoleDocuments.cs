using System.Security.Cryptography;
using System.Text;
using HotChocolate;
using HotChocolate.Language;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The documents the operations console sends, which a host that serves it (<c>UseTraxApiDashboard</c>) registers at
/// startup beside the generated ones, so the console keeps working with persisted operations required. Each is stored
/// under an id in its own reserved prefix that carries the hash of its text, and the console is told the ids in its
/// runtime config.
/// </summary>
/// <remarks>
/// <para>
/// The id is <c>trax_console_&lt;name&gt;_&lt;textHash&gt;&lt;check&gt;</c>, built as a client document's is
/// (<see cref="ClientDocuments.IdOf"/>), so a stored row is a console document only when its body is the one its id
/// names.
/// </para>
/// <para>
/// A console document reaches the operations surface only: one named <c>query</c> or <c>mutation</c> whose every root
/// selection is <c>operations</c>, or a <c>subscription</c> to one of the operator's streams
/// (<see cref="OperatorStreams"/>), with no directive. Persisted-operation management is left out: it runs as text,
/// behind its own gate. A document this host cannot serve (a mutation on a host that exposes only the operations
/// queries, a stream it does not serve) is skipped rather than refusing the start, since the console reaches optional
/// parts of the schema and degrades where they are missing. Registering a document changes which shapes may run,
/// never who may run them: the operations gate decides that on every request. The decision is
/// <c>Trax.Api/docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md</c>.
/// </para>
/// </remarks>
internal static class ConsoleDocuments
{
    /// <summary>The id prefix console documents are stored under, and no uploaded document may use.</summary>
    public const string ReservedIdPrefix = "trax_console_";

    private const string OperationsField = "operations";

    /// <summary>The root subscription fields a console document may select: the operator's streams.</summary>
    public static readonly IReadOnlySet<string> OperatorStreams = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "onTrainStateChanged",
        "onDataChanged",
        "onJunctionEvent",
        "onMachineInstanceChanged",
    };

    /// <summary>Whether <paramref name="id"/> is under the reserved prefix.</summary>
    public static bool IsReserved(string? id) =>
        id is not null && id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal);

    /// <summary>The id of the console document <paramref name="name"/> with <paramref name="text"/>.</summary>
    public static string IdOf(string name, string text)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return $"{SlotOf(name)}_{hash}{TrainDocuments.TextHash(hash, text)}";
    }

    /// <summary>The slot every version of the console document <paramref name="name"/> shares.</summary>
    public static string SlotOf(string name) => ReservedIdPrefix + name.Replace('_', '-');

    /// <summary>
    /// The slot a console id names, or null when the id is not one: the prefix, a non-empty name and a well-formed
    /// hash.
    /// </summary>
    public static string? SlotOfId(string id)
    {
        if (!IsReserved(id) || !MachineDocuments.IsWellFormed(id))
            return null;
        var name = id[ReservedIdPrefix.Length..id.LastIndexOf('_')];
        return name.Length == 0 || name.Contains('_') ? null : id[..id.LastIndexOf('_')];
    }

    /// <summary>The operation name a console id is registered under, or null when the id is not one.</summary>
    public static string? NameOfId(string id) =>
        SlotOfId(id) is { } slot ? slot[ReservedIdPrefix.Length..].Replace('-', '_') : null;

    /// <summary>
    /// The console documents this host can serve, and why each other one is not served. A document is served when it
    /// keeps to the console's rules (<see cref="Refusal"/>) and the host's own validation accepts it.
    /// </summary>
    public static (IReadOnlyList<ConsoleDocument> Served, IReadOnlyList<string> Skipped) Select(
        ISchemaDefinition schema,
        IEnumerable<ConsoleDocumentText> documents
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(documents);

        var served = new List<ConsoleDocument>();
        var skipped = new List<string>();
        foreach (var document in documents)
        {
            var id = IdOf(document.Name, document.Text);
            if (Refusal(schema, document.Name, document.Text) is { } reason)
            {
                skipped.Add($"'{document.Name}' {reason}");
                continue;
            }
            var errors = GeneratedDocumentValidation.Errors(schema, id, document.Text);
            if (errors.Count > 0)
            {
                skipped.Add(
                    $"'{document.Name}' is refused by this host: {string.Join(" ", errors)}"
                );
                continue;
            }
            served.Add(new ConsoleDocument(id, document.Text, document.Name));
        }
        return (served, skipped);
    }

    /// <summary>Why <paramref name="text"/> may not be a console document, or null when it may.</summary>
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
        if (HasDirective(parsed))
            return "uses a directive; a console document uses none.";

        var roots = new List<FieldNode>();
        foreach (var selection in operation.SelectionSet.Selections)
        {
            if (selection is not FieldNode { Alias: null } field)
                return "selects something other than a field at its root.";
            roots.Add(field);
        }
        if (roots.Count == 0)
            return "selects nothing.";

        if (operation.Operation == OperationType.Subscription)
            return
                roots is [var stream]
                && OperatorStreams.Contains(stream.Name.Value)
                && schema.SubscriptionType?.Fields.TryGetField(stream.Name.Value, out _) == true
                ? null
                : "subscribes to something other than one of the operator's streams.";

        var root =
            operation.Operation == OperationType.Mutation ? schema.MutationType : schema.QueryType;
        if (roots.Any(f => f.Name.Value != OperationsField))
            return $"selects something other than '{OperationsField}' at its root.";
        return root?.Fields.TryGetField(OperationsField, out _) == true
            ? null
            : $"reaches '{OperationsField}', which this host does not serve for a {operation.Operation.ToString().ToLowerInvariant()}.";
    }

    private static bool HasDirective(ISyntaxNode node) =>
        node is DirectiveNode || node.GetNodes().Any(HasDirective);
}

/// <summary>One console document this host serves, as <see cref="ConsoleDocuments.Select"/> returns it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Text">The GraphQL document text, as the console sends it.</param>
/// <param name="Name">The operation's name.</param>
internal sealed record ConsoleDocument(string Id, string Text, string Name)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ConsoleDocuments.SlotOf(Name);

    /// <inheritdoc />
    public override string Owner => $"console document '{Name}'";

    /// <inheritdoc />
    public override string Description => $"Sent by the operations console: '{Name}'.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(ConsoleDocuments.IdOf(Name, stored), Id, StringComparison.Ordinal);
}
