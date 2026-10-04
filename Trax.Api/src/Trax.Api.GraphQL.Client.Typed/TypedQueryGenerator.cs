using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Api.GraphQL.Client.Typed;

/// <summary>
/// Walks a request type and its result POCO, emitting a complete GraphQL operation string.
/// The generator handles the 80% case: typed projection of one schema object type, scalar
/// and nested-object property selection, schema-validated arguments. Out of scope for v1:
/// fragments, unions, interfaces, directives. A field is aliased only when <see cref="GraphQLFieldAttribute"/>
/// names a field other than the property's response key, and a result type that refers back to
/// itself is refused, since a selection set must be finite.
///
/// Generation is deterministic and reproducible: the same POCO always produces the same query
/// string. Snapshot tests pin this contract.
/// </summary>
internal static class TypedQueryGenerator
{
    internal sealed record GeneratedQuery(
        string Query,
        string OperationName,
        OperationType OperationType,
        IReadOnlyList<ArgumentBinding> Arguments,
        IReadOnlyList<string> PathSegments,
        string RootField
    );

    internal sealed record ArgumentBinding(
        string VariableName,
        PropertyInfo Property,
        string GraphQLType
    );

    public static GeneratedQuery Generate(Type requestType, Type resultType)
    {
        var opAttr =
            requestType.GetCustomAttribute<GraphQLOperationAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{requestType.FullName}' must be decorated with [GraphQLOperation(OperationType.Query|Mutation)] "
                    + "to be used as a typed request."
            );

        var operationName = opAttr.Name ?? StripRequestSuffix(requestType.Name);
        var rootField = opAttr.RootField ?? CamelCase(operationName);
        var pathSegments = ParsePath(opAttr.Path, requestType);

        var args = CollectArguments(requestType);

        // The result type may legitimately be a list (e.g. IReadOnlyList<Item> for an
        // "allItems" query). Unwrap before requiring [GraphQLType] on the element.
        var elementResultType = UnwrapEnumerable(resultType) ?? resultType;
        var resultTypeAttr =
            elementResultType.GetCustomAttribute<GraphQLTypeAttribute>()
            ?? throw new InvalidOperationException(
                $"Result type '{elementResultType.FullName}' must be decorated with [GraphQLType(\"...\")] "
                    + "to declare which schema type it represents."
            );
        _ = resultTypeAttr;

        var sb = new StringBuilder();
        var keyword = opAttr.OperationType == OperationType.Query ? "query" : "mutation";
        sb.Append(keyword).Append(' ').Append(operationName);

        if (args.Count > 0)
        {
            sb.Append('(');
            for (var i = 0; i < args.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append('$')
                    .Append(args[i].VariableName)
                    .Append(": ")
                    .Append(args[i].GraphQLType);
            }
            sb.Append(')');
        }

        // Wrapper fields (e.g. `discover { netsuite { ... } }`) sit between the operation body
        // and the root field. Their indent grows with depth so the formatted output stays
        // readable. The root field's own indent is 2 spaces per nesting level above its baseline.
        sb.Append(" {\n");

        var rootIndent = 2;
        for (var i = 0; i < pathSegments.Count; i++)
        {
            sb.Append(new string(' ', rootIndent)).Append(pathSegments[i]).Append(" {\n");
            rootIndent += 2;
        }

        sb.Append(new string(' ', rootIndent)).Append(rootField);

        if (args.Count > 0)
        {
            sb.Append('(');
            for (var i = 0; i < args.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                // Convention: the GraphQL argument name on the root field matches the variable
                // name. Consumers whose schema disagrees can override RootField but in practice
                // matching names is the documented pattern.
                sb.Append(args[i].VariableName).Append(": $").Append(args[i].VariableName);
            }
            sb.Append(')');
        }

        sb.Append(" {\n");
        WriteSelectionSet(sb, elementResultType, indent: rootIndent + 2);
        sb.Append(new string(' ', rootIndent)).Append("}\n");

        for (var i = pathSegments.Count - 1; i >= 0; i--)
        {
            rootIndent -= 2;
            sb.Append(new string(' ', rootIndent)).Append("}\n");
        }

        sb.Append("}\n");

        return new GeneratedQuery(
            sb.ToString(),
            operationName,
            opAttr.OperationType,
            args,
            pathSegments,
            rootField
        );
    }

    private static IReadOnlyList<string> ParsePath(string? path, Type requestType)
    {
        if (path is null)
            return Array.Empty<string>();

        // Empty is operator error: setting Path at all means you intend to nest. The way to
        // opt out is to omit the property, which leaves it null.
        if (path.Length == 0)
            throw new InvalidOperationException(
                $"[GraphQLOperation] on '{requestType.FullName}' has Path = \"\". "
                    + "To opt out of nesting, omit the Path property entirely. "
                    + "To nest, set Path to a dot-separated chain such as Path = \"discover.netsuite\"."
            );

        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (seg.Length == 0)
                throw new InvalidOperationException(
                    $"[GraphQLOperation] on '{requestType.FullName}' has malformed Path = \"{path}\" "
                        + "(empty segment from a leading, trailing, or doubled dot). "
                        + "Each dot-separated segment must be a non-empty GraphQL field name, e.g. \"discover.netsuite\"."
                );

            for (var c = 0; c < seg.Length; c++)
            {
                if (char.IsWhiteSpace(seg[c]))
                    throw new InvalidOperationException(
                        $"[GraphQLOperation] on '{requestType.FullName}' has whitespace in Path = \"{path}\". "
                            + "Path segments must be valid GraphQL field names with no whitespace, e.g. \"discover.netsuite\"."
                    );
            }
        }

        return segments;
    }

    private static IReadOnlyList<ArgumentBinding> CollectArguments(Type requestType)
    {
        var result = new List<ArgumentBinding>();
        foreach (var prop in requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<GraphQLArgumentAttribute>();
            if (attr is null)
                continue;
            var name = attr.VariableName ?? CamelCase(prop.Name);
            result.Add(new ArgumentBinding(name, prop, attr.GraphQLType));
        }
        return result;
    }

    private static void WriteSelectionSet(StringBuilder sb, Type type, int indent) =>
        WriteSelectionSet(sb, type, indent, new List<(Type Type, string? Via)> { (type, null) });

    // `path` holds the object types from the result type down to `type`, each with the
    // property that reached the next one. A GraphQL selection set is explicit and finite (the
    // spec's NoFragmentCycles rule exists for the same reason), so a property that leads back
    // to a type already on the path has no query to generate and is refused, naming the loop.
    // The same type under two sibling properties is not on one path and is fine.
    private static void WriteSelectionSet(
        StringBuilder sb,
        Type type,
        int indent,
        List<(Type Type, string? Via)> path
    )
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // An indexer is no member System.Text.Json reads, so it has no field to select.
            if (IsNeverRead(prop) || prop.GetIndexParameters().Length > 0)
                continue;

            var responseKey =
                prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? CamelCase(prop.Name);
            var fieldName = prop.GetCustomAttribute<GraphQLFieldAttribute>()?.FieldName;

            var elementType = UnwrapEnumerable(prop.PropertyType);
            var nested = elementType ?? prop.PropertyType;
            var underlying = Nullable.GetUnderlyingType(nested) ?? nested;

            sb.Append(new string(' ', indent));
            if (fieldName is null || fieldName == responseKey)
            {
                sb.Append(responseKey);
            }
            else
            {
                // [GraphQLField] names the schema field; the response must still come back under
                // the key the deserializer binds this property to, so alias the field to it.
                if (!IsGraphQLName(responseKey))
                    throw new InvalidOperationException(
                        $"'{type.Name}.{prop.Name}' selects field '{fieldName}' under the response key "
                            + $"'{responseKey}', which is not a valid GraphQL alias. Give the property a "
                            + "[JsonPropertyName] that is a GraphQL name (letters, digits and '_', not "
                            + "starting with a digit), or one equal to the field name."
                    );
                sb.Append(responseKey).Append(": ").Append(fieldName);
            }

            if (HasGraphQLType(underlying))
            {
                var loopStart = path.FindIndex(p => p.Type == underlying);
                if (loopStart >= 0)
                    throw new InvalidOperationException(
                        $"Result type '{path[0].Type.FullName}' cannot be turned into a query because it "
                            + $"refers back to itself: {DescribeLoop(path, loopStart, prop, underlying)}. "
                            + "A GraphQL selection must be finite. Give the nested level its own type "
                            + "that stops where the query should stop, or mark the property "
                            + "[JsonIgnore] to leave it out."
                    );

                sb.Append(" {\n");
                path[^1] = (path[^1].Type, prop.Name);
                path.Add((underlying, null));
                WriteSelectionSet(sb, underlying, indent + 2, path);
                path.RemoveAt(path.Count - 1);
                path[^1] = (path[^1].Type, null);
                sb.Append(new string(' ', indent)).Append('}');
            }

            sb.Append('\n');
        }
    }

    // Only Condition = Always keeps System.Text.Json from reading a property. WhenWritingNull,
    // WhenWritingDefault and Never affect serialization only, so such a property is still
    // populated from the response and has to be selected.
    private static bool IsNeverRead(PropertyInfo prop) =>
        prop.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always;

    private static string DescribeLoop(
        List<(Type Type, string? Via)> path,
        int loopStart,
        PropertyInfo closing,
        Type target
    )
    {
        var sb = new StringBuilder();
        for (var i = loopStart; i < path.Count - 1; i++)
            sb.Append(path[i].Type.Name).Append('.').Append(path[i].Via).Append(" -> ");
        sb.Append(path[^1].Type.Name).Append('.').Append(closing.Name).Append(" -> ");
        sb.Append(target.Name);
        return sb.ToString();
    }

    private static bool IsGraphQLName(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]))
            return false;
        foreach (var c in name)
        {
            if (!(c is '_' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
                return false;
        }
        return true;
    }

    // An object type gets a subselection; anything else is a leaf. A type marked [GraphQLType] is
    // an object type. Otherwise a primitive, string, enum or struct is a leaf, and so is every
    // BCL class (Uri, Version, JsonObject, ...): those map to scalars (URL, String, JSON) on the
    // server, and selecting their CLR properties would ask for fields no schema has.
    private static bool HasGraphQLType(Type t)
    {
        if (t.GetCustomAttribute<GraphQLTypeAttribute>() is not null)
            return true;
        if (!t.IsClass || t == typeof(string) || t == typeof(object) || IsBclType(t))
            return false;
        return t.GetProperties().Any(p => p.GetIndexParameters().Length == 0);
    }

    private static bool IsBclType(Type t) =>
        t.Namespace is { } ns
        && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal));

    private static Type? UnwrapEnumerable(Type t)
    {
        if (t == typeof(string))
            return null;
        if (t.IsArray)
            return t.GetElementType();
        foreach (var iface in t.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }
        return null;
    }

    // The naming HotChocolate gives a C# member and System.Text.Json's camel case agree: a leading
    // acronym is lowered as a whole (ID -> id, URLPath -> urlPath), not just its first letter.
    private static string CamelCase(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string StripRequestSuffix(string name) =>
        name.EndsWith("Request", StringComparison.Ordinal) ? name[..^"Request".Length] : name;
}
