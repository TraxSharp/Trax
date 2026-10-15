using System.Security.Cryptography;
using System.Text;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using Trax.Api.GraphQL.TypeModules;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Generates the persisted document of every train a host exposes with <c>[TraxMutation]</c> or <c>[TraxQuery]</c>,
/// each under an id in its own reserved prefix that carries a hash of the train's shape, so a client built against one
/// shape of a train is answered <c>CLIENT_OUTDATED</c> once the shape changes, never a validation error.
/// </summary>
/// <remarks>
/// <para>
/// The id is <c>trax_train_&lt;root&gt;_&lt;namespace&gt;_&lt;field&gt;_&lt;shapeHash&gt;&lt;textHash&gt;</c>: the root
/// (<c>mutation</c> or <c>query</c>), the namespace's field (empty without one), the train's field, the SHA-256 of its
/// shape, and 16 hex characters of the SHA-256 of the shape hash and the text. A <c>_</c> in a GraphQL name is written
/// <c>-</c>, which no GraphQL name holds, so the segments always read back. The shape is the field's signature and the
/// SDL of every type its arguments and its selection reach, without descriptions and without the root and namespace
/// types that hold every other train: adding a field to the train's input moves the id, editing a doc comment or
/// exposing another train does not.
/// </para>
/// <para>
/// A document takes one variable per argument, named like it (<c>input</c>, <c>mode</c>, <c>priority</c>), and selects
/// every field of the answer at every depth, a union's or an interface's through an inline fragment per possible type.
/// An answer that holds itself, or a field that takes a required argument, has no finite document, so it refuses the
/// host's start, naming the train and the type.
/// </para>
/// </remarks>
internal static class TrainDocuments
{
    /// <summary>The id prefix every train document is stored under, and no uploaded document may use.</summary>
    public const string ReservedIdPrefix = "trax_train_";

    private const string MutationRootField = "dispatch";
    private const string QueryRootField = "discover";

    /// <summary>Whether <paramref name="id"/> is under the reserved prefix.</summary>
    public static bool IsReserved(string? id) =>
        id is not null && id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The documents of every train in <paramref name="names"/> that the schema serves, ordered by their id's slot.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A train answers with a type that holds itself, or with a field that takes a required argument.
    /// </exception>
    public static IReadOnlyList<TrainDocument> Generate(
        ISchemaDefinition schema,
        IReadOnlyDictionary<TrainRegistration, string> names
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(names);

        var sdl = DocumentTypes.Definitions(schema);
        var wrappers = DocumentTypes.Wrappers(schema);
        var documents = new List<TrainDocument>();
        foreach (var (registration, trainName) in names)
        {
            if (!registration.IsMutation && !registration.IsQuery)
                continue;
            var root = registration.IsMutation ? "mutation" : "query";
            var ns = registration.GraphQLNamespace is { } n ? TrainTypeModule.CamelCase(n) : null;
            var field = TrainTypeModule.CamelCase(trainName);
            if (FieldOf(schema, registration.IsMutation, ns, field) is not { } definition)
                continue;

            var text = Document(schema, root, ns, field, definition, registration);
            var shape = ShapeHash(schema, sdl, wrappers, definition, text);
            documents.Add(
                new TrainDocument(
                    IdOf(root, ns, field, shape, text),
                    root,
                    ns,
                    field,
                    text,
                    shape,
                    Print(definition.Type),
                    registration
                )
            );
        }
        return documents.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The id of a train document.</summary>
    public static string IdOf(
        string root,
        string? ns,
        string field,
        string shapeHash,
        string text
    ) => $"{SlotOf(root, ns, field)}_{shapeHash}{TextHash(shapeHash, text)}";

    /// <summary>
    /// The slot of a train's documents: its id without the hash, which every version of the train's document shares.
    /// </summary>
    public static string SlotOf(string root, string? ns, string field) =>
        $"{ReservedIdPrefix}{root}_{Encode(ns ?? "")}_{Encode(field)}";

    /// <summary>
    /// The slot a train id names, or null when the id is not one: the prefix, a root of <c>mutation</c> or
    /// <c>query</c>, a namespace segment (empty or not), a non-empty field and a well-formed hash.
    /// </summary>
    public static string? SlotOf(string id)
    {
        if (!IsReserved(id) || !MachineDocuments.IsWellFormed(id))
            return null;
        var parts = id[ReservedIdPrefix.Length..].Split('_');
        if (parts.Length != 4 || parts[0] is not ("mutation" or "query") || parts[2].Length == 0)
            return null;
        return id[..id.LastIndexOf('_')];
    }

    // A GraphQL name never holds '-', so writing '_' as '-' keeps every segment free of the separator and reads back
    // to one name only.
    private static string Encode(string name) => name.Replace('_', '-');

    internal static string TextHash(string shapeHash, string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shapeHash + "\n" + text)))[
            ..MachineDocuments.TextHashLength
        ];

    private static IOutputFieldDefinition? FieldOf(
        ISchemaDefinition schema,
        bool mutation,
        string? ns,
        string field
    )
    {
        var root = mutation ? schema.MutationType : schema.QueryType;
        if (
            root is null
            || !root.Fields.TryGetField(
                mutation ? MutationRootField : QueryRootField,
                out var parent
            )
            || parent.Type.NamedType() is not IObjectTypeDefinition parentType
        )
            return null;
        var holder = parentType;
        if (ns is not null)
        {
            if (
                !parentType.Fields.TryGetField(ns, out var nsField)
                || nsField.Type.NamedType() is not IObjectTypeDefinition nsType
            )
                return null;
            holder = nsType;
        }
        return holder.Fields.TryGetField(field, out var definition) ? definition : null;
    }

    private static string Document(
        ISchemaDefinition schema,
        string root,
        string? ns,
        string field,
        IOutputFieldDefinition definition,
        TrainRegistration registration
    )
    {
        var name = Pascal(ns ?? "") + Pascal(field);
        var arguments = definition.Arguments.ToList();
        var text = new StringBuilder();
        text.Append(root).Append(' ').Append(name);
        if (arguments.Count > 0)
            text.Append('(')
                .AppendJoin(", ", arguments.Select(a => $"${a.Name}: {Print(a.Type)}"))
                .Append(')');
        text.Append(" { ")
            .Append(root == "mutation" ? MutationRootField : QueryRootField)
            .Append(" { ");
        if (ns is not null)
            text.Append(ns).Append(" { ");
        text.Append(field);
        if (arguments.Count > 0)
            text.Append('(')
                .AppendJoin(", ", arguments.Select(a => $"{a.Name}: ${a.Name}"))
                .Append(')');
        text.Append(
                Selection(
                    schema,
                    definition.Type,
                    [],
                    $"The train '{registration.ServiceType.FullName}'"
                )
            )
            .Append(" }");
        if (ns is not null)
            text.Append(" }");
        text.Append(" }");
        return text.ToString();
    }

    // Every field of an object type at every depth, an inline fragment per possible type of a union or interface, and
    // nothing for a leaf. A type met again below itself, or a field that takes a required argument, has no finite
    // document, so it refuses the train.
    internal static string Selection(
        ISchemaDefinition schema,
        IType type,
        List<string> ancestors,
        string owner
    )
    {
        switch (type.NamedType())
        {
            case IObjectTypeDefinition objectType:
                return " { " + Fields(schema, objectType, ancestors, owner) + " }";
            case IUnionTypeDefinition union:
                return " { __typename "
                    + string.Join(
                        " ",
                        union
                            .Types.OrderBy(t => t.Name, StringComparer.Ordinal)
                            .Select(t =>
                                $"... on {t.Name} {{ {Fields(schema, t, ancestors, owner)} }}"
                            )
                    )
                    + " }";
            case IInterfaceTypeDefinition @interface:
                return " { __typename "
                    + string.Join(
                        " ",
                        Implementations(schema, @interface)
                            .Select(t =>
                                $"... on {t.Name} {{ {Fields(schema, t, ancestors, owner)} }}"
                            )
                    )
                    + " }";
            default:
                return "";
        }
    }

    private static string Fields(
        ISchemaDefinition schema,
        IObjectTypeDefinition objectType,
        List<string> ancestors,
        string owner
    )
    {
        if (ancestors.Contains(objectType.Name, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"{owner} answers with the type {objectType.Name}, which holds "
                    + $"itself ({string.Join(" > ", ancestors.Append(objectType.Name))}), so no finite document "
                    + "selects it. An exposed train's answer needs a shape that ends: flatten it, or stop exposing "
                    + "the train."
            );
        ancestors.Add(objectType.Name);
        var fields = new List<string>();
        foreach (var field in objectType.Fields)
        {
            if (field.Name.StartsWith("__", StringComparison.Ordinal))
                continue;
            if (
                field.Arguments.FirstOrDefault(a =>
                    a.Type.Kind == TypeKind.NonNull && a.DefaultValue is null
                ) is
                { } required
            )
                throw new InvalidOperationException(
                    $"{owner} answers with {objectType.Name}.{field.Name}, which "
                        + $"takes the required argument '{required.Name}', so its document cannot select it. Give the "
                        + "argument a default, or leave the field out of the train's output."
                );
            fields.Add(field.Name + Selection(schema, field.Type, ancestors, owner));
        }
        ancestors.RemoveAt(ancestors.Count - 1);
        return fields.Count == 0 ? "__typename" : string.Join(" ", fields);
    }

    private static IEnumerable<IObjectTypeDefinition> Implementations(
        ISchemaDefinition schema,
        IInterfaceTypeDefinition @interface
    ) =>
        schema
            .Types.OfType<IObjectTypeDefinition>()
            .Where(t => t.Implements.Any(i => i.Name == @interface.Name))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    // The field's signature, then the definition of every type the document reaches but the root and namespace types,
    // in name order, without descriptions.
    internal static string ShapeHash(
        ISchemaDefinition schema,
        IReadOnlyDictionary<string, INamedSyntaxNode> sdl,
        IReadOnlySet<string> wrappers,
        IOutputFieldDefinition definition,
        string text
    )
    {
        var reached = DocumentTypes.Reached(schema, [text]);
        var shape = new StringBuilder();
        shape.Append(definition.Name);
        foreach (var argument in definition.Arguments)
            shape
                .Append(' ')
                .Append(argument.Name)
                .Append(':')
                .Append(Print(argument.Type))
                .Append('=')
                .Append(argument.DefaultValue?.ToString() ?? "");
        shape.Append("->").Append(Print(definition.Type)).Append('\n');
        foreach (var name in reached)
            if (sdl.TryGetValue(name, out var node) && !wrappers.Contains(name))
                shape.Append(DocumentTypes.WithoutDescriptions(node)).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shape.ToString())));
    }

    private static string Pascal(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static string Print(IType type) =>
        type.Kind switch
        {
            TypeKind.NonNull => Print(type.InnerType()) + "!",
            TypeKind.List => "[" + Print(type.InnerType()) + "]",
            _ => type.NamedType().Name,
        };
}

/// <summary>One generated train document, as <see cref="TrainDocuments.Generate"/> returns it.</summary>
/// <param name="Id">The persisted id.</param>
/// <param name="Root"><c>mutation</c> or <c>query</c>.</param>
/// <param name="Namespace">The namespace's field, or null.</param>
/// <param name="Field">The train's field.</param>
/// <param name="Text">The GraphQL document text.</param>
/// <param name="ShapeHash">The hash of the train's shape the id carries.</param>
/// <param name="Answer">The field's type as SDL writes it (<c>StartRunResponse!</c>).</param>
/// <param name="Registration">The train.</param>
internal sealed record TrainDocument(
    string Id,
    string Root,
    string? Namespace,
    string Field,
    string Text,
    string ShapeHash,
    string Answer,
    TrainRegistration Registration
) : GeneratedDocument(Id, Text)
{
    /// <summary>The id without its hash, which every version of this train's document shares.</summary>
    public override string Slot => Id[..Id.LastIndexOf('_')];

    /// <inheritdoc />
    public override string Owner => $"train '{Registration.ServiceType.FullName}'";

    /// <inheritdoc />
    public override string Description =>
        $"Generated for the train '{Registration.ServiceType.FullName}' ({Root} {Field}), shape {ShapeHash}.";

    /// <inheritdoc />
    public override bool Matches(string stored) =>
        string.Equals(
            TrainDocuments.IdOf(Root, Namespace, Field, ShapeHash, stored),
            Id,
            StringComparison.Ordinal
        );
}
