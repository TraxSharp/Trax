using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Generates the persisted document of each view a host serves from features (ADR <c>Trax.Docs/adr/0058</c>): one query
/// per field of the <c>views</c> root, selecting everything the view shows. The view's own read rule still decides who
/// reads it, in its resolver; the document only lets a host that requires persisted operations run it.
/// </summary>
/// <remarks>
/// The id is <c>trax_view_&lt;field&gt;_&lt;shapeHash&gt;&lt;textHash&gt;</c>, the field being the view's camelCase
/// field, which holds no <c>_</c>, and the hashes made as every generated id's are (<see cref="TrainDocuments.TextHash"/>),
/// so a client built against one shape of a view is told <c>CLIENT_OUTDATED</c> once it changes.
/// </remarks>
internal static class ViewDocuments
{
    /// <summary>The id prefix view documents are stored under, and no uploaded document may use.</summary>
    public const string ReservedIdPrefix = "trax_view_";

    /// <summary>Whether <paramref name="id"/> is under the reserved prefix.</summary>
    public static bool IsReserved(string? id) =>
        id is not null && id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal);

    /// <summary>One document per view the schema serves, in field order; none when it serves no <c>views</c> root.</summary>
    public static IReadOnlyList<ViewDocument> Generate(ISchemaDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (
            schema.QueryType is not { } query
            || !query.Fields.TryGetField(MachineViewTypeModule.RootField, out var root)
            || root.Type.NamedType() is not IObjectTypeDefinition views
        )
            return [];

        var sdl = DocumentTypes.Definitions(schema);
        var wrappers = DocumentTypes.Wrappers(schema);
        var documents = new List<ViewDocument>();
        foreach (
            var view in views
                .Fields.Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
        )
        {
            var text =
                $"query {char.ToUpperInvariant(view.Name[0])}{view.Name[1..]}View {{ {MachineViewTypeModule.RootField} {{ {view.Name}"
                + DocumentSelection.Selection(
                    schema,
                    view.Type,
                    new SelectionWalk($"The view {view.Name}")
                )
                + " } }";
            var shape = DocumentSelection.ShapeHash(schema, sdl, wrappers, view, text);
            documents.Add(
                new ViewDocument(
                    $"{SlotFor(view.Name)}_{shape}{TrainDocuments.TextHash(shape, text)}",
                    view.Name,
                    text,
                    shape
                )
            );
        }
        return documents;
    }

    /// <summary>The slot of a view's documents: its id without the hash.</summary>
    public static string SlotFor(string field) => $"{ReservedIdPrefix}{field}";

    /// <summary>The slot a view id names, or null when the id is not one: the prefix, a field and a well-formed hash.</summary>
    public static string? SlotOf(string id)
    {
        if (!IsReserved(id) || !MachineDocuments.IsWellFormed(id))
            return null;
        var parts = id[ReservedIdPrefix.Length..].Split('_');
        return parts.Length == 2 && parts[0].Length > 0 ? id[..id.LastIndexOf('_')] : null;
    }
}

/// <summary>One generated view document, as <see cref="ViewDocuments.Generate"/> returns it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Field">The view's field under the <c>views</c> root.</param>
/// <param name="Text">The GraphQL document text.</param>
/// <param name="ShapeHash">The hash of the view's shape the id carries.</param>
internal sealed record ViewDocument(string Id, string Field, string Text, string ShapeHash)
    : GeneratedDocument(Id, Text)
{
    /// <inheritdoc />
    public override string Slot => ViewDocuments.SlotFor(Field);

    /// <inheritdoc />
    public override string Owner => $"view '{Field}'";

    /// <inheritdoc />
    public override string Description => $"Generated for the view '{Field}', shape {ShapeHash}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(
            $"{Slot}_{ShapeHash}{TrainDocuments.TextHash(ShapeHash, stored)}",
            Id,
            StringComparison.Ordinal
        );
}
