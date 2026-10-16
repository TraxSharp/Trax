using System.Security.Cryptography;
using System.Text;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
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
/// A field that leads back to a type already on the path (an entity's reference to its parent, a tree's children) is
/// left out, so the document ends; a paged field counts as its node type. Each document lists the fields it left out.
/// A field that takes a required argument has no value to select it with, so it refuses the host's start, naming the
/// train and the field.
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
    /// A train answers with a field that takes a required argument.
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

            var walk = new SelectionWalk($"The train '{registration.ServiceType.FullName}'");
            var text = Document(schema, root, ns, field, definition, walk);
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
                    walk.LeftOut,
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
        SelectionWalk walk
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
        text.Append(Selection(schema, definition.Type, walk)).Append(" }");
        if (ns is not null)
            text.Append(" }");
        text.Append(" }");
        return text.ToString();
    }

    // Every field of an object type at every depth, an inline fragment per possible type of a union or interface, and
    // nothing for a leaf. A field or a possible type that leads back to a type already on the path is left out and
    // recorded in the walk, so the document ends. A field that takes a required argument refuses the train.
    internal static string Selection(ISchemaDefinition schema, IType type, SelectionWalk walk)
    {
        switch (type.NamedType())
        {
            case IObjectTypeDefinition objectType:
                return " { " + Fields(schema, objectType, walk) + " }";
            case IUnionTypeDefinition union:
                return PossibleTypes(
                    schema,
                    union.Types.OrderBy(t => t.Name, StringComparer.Ordinal),
                    walk
                );
            case IInterfaceTypeDefinition @interface:
                return PossibleTypes(schema, Implementations(schema, @interface), walk);
            default:
                return "";
        }
    }

    private static string PossibleTypes(
        ISchemaDefinition schema,
        IEnumerable<IObjectTypeDefinition> types,
        SelectionWalk walk
    )
    {
        var fragments = new List<string>();
        foreach (var possible in types)
        {
            if (walk.IsOnPath(possible.Name))
            {
                walk.Leave($"... on {possible.Name}");
                continue;
            }
            fragments.Add($"... on {possible.Name} {{ {Fields(schema, possible, walk)} }}");
        }
        return " { __typename" + string.Concat(fragments.Select(f => " " + f)) + " }";
    }

    private static string Fields(
        ISchemaDefinition schema,
        IObjectTypeDefinition objectType,
        SelectionWalk walk
    )
    {
        walk.Enter(objectType.Name);
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
                    $"{walk.Owner} answers with {objectType.Name}.{field.Name}, which "
                        + $"takes the required argument '{required.Name}', so its document cannot select it. Give the "
                        + "argument a default, or leave the field out of the train's output."
                );
            var segment = $"{objectType.Name}.{field.Name}";
            if (walk.IsOnPath(ItemOf(field.Type).Name))
            {
                walk.Leave(segment);
                continue;
            }
            walk.Take(segment);
            fields.Add(field.Name + Selection(schema, field.Type, walk));
            walk.Return();
        }
        walk.Exit();
        return fields.Count == 0 ? "__typename" : string.Join(" ", fields);
    }

    // The type a field holds, a connection's being its node type, so a paged back-reference is left out whole rather
    // than leaving its paging wrapper behind.
    private static ITypeDefinition ItemOf(IType type) =>
        type.NamedType() is IConnectionType connection
            ? ItemOf(connection.EdgeType.NodeType)
            : type.NamedType();

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
/// <param name="LeftOut">
/// The fields the document leaves out because they lead back to a type already on the path, each as the path that
/// reaches it (<c>Order.customer &gt; Customer.orders</c>).
/// </param>
/// <param name="Registration">The train.</param>
internal sealed record TrainDocument(
    string Id,
    string Root,
    string? Namespace,
    string Field,
    string Text,
    string ShapeHash,
    string Answer,
    IReadOnlyList<string> LeftOut,
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

/// <summary>
/// Where a walk through an answer is: the object types above the field being selected, the fields taken to reach it,
/// and every field left out so far because it leads back to one of those types.
/// </summary>
/// <param name="owner">What the document is generated for, as a refusal names it (<c>The train 'X'</c>).</param>
internal sealed class SelectionWalk(string owner)
{
    private readonly List<string> _types = [];
    private readonly List<string> _trail = [];
    private readonly List<string> _leftOut = [];

    /// <summary>What the document is generated for, as a refusal names it.</summary>
    public string Owner { get; } = owner;

    /// <summary>Every field left out, as the path that reaches it, in the order the walk met them.</summary>
    public IReadOnlyList<string> LeftOut => _leftOut;

    /// <summary>Whether <paramref name="typeName"/> is one of the object types the walk is inside.</summary>
    public bool IsOnPath(string typeName) => _types.Contains(typeName, StringComparer.Ordinal);

    public void Enter(string typeName) => _types.Add(typeName);

    public void Exit() => _types.RemoveAt(_types.Count - 1);

    public void Take(string segment) => _trail.Add(segment);

    public void Return() => _trail.RemoveAt(_trail.Count - 1);

    /// <summary>Records <paramref name="segment"/>, reached by the fields taken so far, as left out.</summary>
    public void Leave(string segment) => _leftOut.Add(string.Join(" > ", _trail.Append(segment)));
}
