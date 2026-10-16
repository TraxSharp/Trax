using System.Security.Cryptography;
using System.Text;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The selection of everything a fixed, Trax-owned answer holds (<c>runProgress</c>'s), and the hash of the shape it
/// reaches. Used only where the answer is a type Trax declares, so its size is known: it walks every path, so an
/// answer that reaches a consumer's entity graph would grow with the number of paths through it, which is why no
/// consumer type is selected this way (see <c>Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md</c>).
/// </summary>
internal static class DocumentSelection
{
    // Every field of an object type at every depth, an inline fragment per possible type of a union or interface, and
    // nothing for a leaf. A field or a possible type that leads back to a type already on the path is left out and
    // recorded in the walk, so the document ends. A field that takes a required argument refuses the train.
    public static string Selection(ISchemaDefinition schema, IType type, SelectionWalk walk)
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
    public static string ShapeHash(
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

    private static string Print(IType type) =>
        type.Kind switch
        {
            TypeKind.NonNull => Print(type.InnerType()) + "!",
            TypeKind.List => "[" + Print(type.InnerType()) + "]",
            _ => type.NamedType().Name,
        };
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
