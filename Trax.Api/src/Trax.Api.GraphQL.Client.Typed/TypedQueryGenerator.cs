using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace Trax.Api.GraphQL.Client.Typed;

/// <summary>
/// Walks a request type and its result POCO, emitting a complete GraphQL operation string.
/// The generator handles the 80% case: typed projection of one schema object type, scalar
/// and nested-object property selection, schema-validated arguments. Out of scope for v1:
/// fragments, unions, interfaces, aliases, directives.
///
/// Generation is deterministic and reproducible: the same POCO always produces the same query
/// string. Snapshot tests pin this contract.
/// </summary>
internal static class TypedQueryGenerator
{
    public sealed record GeneratedQuery(
        string Query,
        string OperationName,
        OperationType OperationType,
        IReadOnlyList<ArgumentBinding> Arguments,
        IReadOnlyList<string> PathSegments,
        string RootField
    );

    public sealed record ArgumentBinding(
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

    private static void WriteSelectionSet(StringBuilder sb, Type type, int indent)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                continue;

            var name =
                prop.GetCustomAttribute<GraphQLFieldAttribute>()?.FieldName
                ?? prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? CamelCase(prop.Name);

            var elementType = UnwrapEnumerable(prop.PropertyType);
            var nested = elementType ?? prop.PropertyType;
            var underlying = Nullable.GetUnderlyingType(nested) ?? nested;

            sb.Append(new string(' ', indent)).Append(name);

            if (HasGraphQLType(underlying))
            {
                sb.Append(" {\n");
                WriteSelectionSet(sb, underlying, indent + 2);
                sb.Append(new string(' ', indent)).Append('}');
            }

            sb.Append('\n');
        }
    }

    private static bool HasGraphQLType(Type t)
    {
        if (t.IsPrimitive || t == typeof(string) || t.IsEnum || t == typeof(decimal))
            return false;
        return t.GetCustomAttribute<GraphQLTypeAttribute>() is not null
            || (t.IsClass && t != typeof(object) && t.GetProperties().Length > 0);
    }

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

    private static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string StripRequestSuffix(string name) =>
        name.EndsWith("Request", StringComparison.Ordinal) ? name[..^"Request".Length] : name;
}
