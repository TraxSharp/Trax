using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Generates the two persisted documents of <c>runProgress</c>, a first read and a stream, on a host where a train
/// lets its starter follow it. One pair serves every such train, since the run id alone names the run.
/// </summary>
/// <remarks>
/// The id is <c>trax_progress_&lt;operation&gt;_&lt;shapeHash&gt;&lt;textHash&gt;</c>, the operation being
/// <c>query</c> or <c>subscription</c>, and the hashes made as a train document's are. Each selects every field of the
/// answer, so a client built against one version of the answer is told <c>CLIENT_OUTDATED</c> once it changes.
/// </remarks>
internal static class ProgressDocuments
{
    /// <summary>The id prefix the progress documents are stored under, and no uploaded document may use.</summary>
    public const string ReservedIdPrefix = "trax_progress_";

    private const string Owner = "runProgress";

    /// <summary>Whether <paramref name="id"/> is under the reserved prefix.</summary>
    public static bool IsReserved(string? id) =>
        id is not null && id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The query's and the subscription's documents, or none when the schema serves no <c>runProgress</c>.
    /// </summary>
    public static IReadOnlyList<ProgressDocument> Generate(ISchemaDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var sdl = DocumentTypes.Definitions(schema);
        var wrappers = DocumentTypes.Wrappers(schema);
        var documents = new List<ProgressDocument>();
        foreach (
            var (operation, root, name) in new[]
            {
                ("query", schema.QueryType, "RunProgress"),
                ("subscription", schema.SubscriptionType, "RunProgressEvents"),
            }
        )
        {
            if (
                root is null
                || !root.Fields.TryGetField(RunProgressTypeModule.Field, out var definition)
            )
                continue;
            var text =
                $"{operation} {name}($runId: String!) {{ {RunProgressTypeModule.Field}(runId: $runId)"
                + TrainDocuments.Selection(
                    schema,
                    definition.Type,
                    new SelectionWalk($"The {Owner} {operation}")
                )
                + " }";
            var shape = TrainDocuments.ShapeHash(schema, sdl, wrappers, definition, text);
            documents.Add(
                new ProgressDocument(
                    $"{SlotFor(operation)}_{shape}{TrainDocuments.TextHash(shape, text)}",
                    operation,
                    text,
                    shape
                )
            );
        }
        return documents;
    }

    /// <summary>The slot of an operation's documents: its id without the hash.</summary>
    public static string SlotFor(string operation) => $"{ReservedIdPrefix}{operation}";

    /// <summary>
    /// The slot a progress id names, or null when the id is not one: the prefix, <c>query</c> or
    /// <c>subscription</c>, and a well-formed hash.
    /// </summary>
    public static string? SlotOf(string id)
    {
        if (!IsReserved(id) || !MachineDocuments.IsWellFormed(id))
            return null;
        var parts = id[ReservedIdPrefix.Length..].Split('_');
        return parts.Length == 2 && parts[0] is ("query" or "subscription")
            ? id[..id.LastIndexOf('_')]
            : null;
    }
}

/// <summary>One generated runProgress document, as <see cref="ProgressDocuments.Generate"/> returns it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Operation"><c>query</c> or <c>subscription</c>.</param>
/// <param name="Text">The GraphQL document text.</param>
/// <param name="ShapeHash">The hash of the answer's shape the id carries.</param>
internal sealed record ProgressDocument(string Id, string Operation, string Text, string ShapeHash)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ProgressDocuments.SlotFor(Operation);

    /// <inheritdoc />
    public override string Owner => $"runProgress {Operation}";

    /// <inheritdoc />
    public override string Description =>
        $"Generated for runProgress ({Operation}), shape {ShapeHash}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(
            $"{Slot}_{ShapeHash}{TrainDocuments.TextHash(ShapeHash, stored)}",
            Id,
            StringComparison.Ordinal
        );
}
