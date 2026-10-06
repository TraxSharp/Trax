using System.Text;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Converts a parsed introspection result into a GraphQL SDL string suitable for
/// <c>Schema.For(sdl)</c>. Covers everything the validator needs: types, fields, input
/// objects, enums, unions, interfaces, type references, deprecations, default values.
///
/// What we intentionally drop:
///   - Descriptions (validator doesn't need them; saves complexity around escaping)
///   - Introspection meta-types (<c>__Schema</c>, <c>__Type</c>, ...)
///   - Built-in scalars (<c>String, Int, Float, Boolean, ID</c>) — Schema.For provides them
///   - Custom directive definitions — built-ins (<c>@include, @skip, @deprecated</c>) are
///     auto-provided; custom directives are rare in client-side validation.
/// </summary>
internal static class IntrospectionSdlBuilder
{
    private static readonly HashSet<string> BuiltinScalars = new(StringComparer.Ordinal)
    {
        "String",
        "Int",
        "Float",
        "Boolean",
        "ID",
    };

    /// <summary>
    /// Returns true for GraphQL's five spec-defined scalars. Used by
    /// <see cref="IntrospectingSchemaProvider"/> to decide which scalars need a
    /// runtime <c>IGraphType</c> registration — the built-ins ship with graphql-dotnet,
    /// everything else (Any, DateTime, Uuid, JSON, ...) does not.
    /// </summary>
    internal static bool IsBuiltinScalar(string name) => BuiltinScalars.Contains(name);

    public static string Build(IntrospectionSchema schema)
    {
        var sb = new StringBuilder();

        // Always emit the schema block: required when subscription/mutation root types are
        // present, and never harmful when they aren't.
        sb.Append("schema {\n");
        sb.Append("  query: ").Append(schema.QueryType.Name).Append('\n');
        if (schema.MutationType?.Name is { } mn)
            sb.Append("  mutation: ").Append(mn).Append('\n');
        if (schema.SubscriptionType?.Name is { } sn)
            sb.Append("  subscription: ").Append(sn).Append('\n');
        sb.Append("}\n\n");

        foreach (var type in schema.Types)
        {
            if (type.Name is null)
                continue;
            if (type.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            // graphql-dotnet's Schema.For() requires explicit declarations for ID (and is
            // happy to accept redundant declarations of the other built-in scalars). Emit
            // them all rather than risking the "Unknown type ID" parse error.
            _ = BuiltinScalars;
            WriteType(sb, type);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static void WriteType(StringBuilder sb, IntrospectionType type)
    {
        switch (type.Kind)
        {
            case "SCALAR":
                sb.Append("scalar ").Append(type.Name).Append('\n');
                break;

            case "OBJECT":
                sb.Append("type ").Append(type.Name);
                if (type.Interfaces is { Count: > 0 })
                {
                    sb.Append(" implements ");
                    sb.AppendJoin(" & ", type.Interfaces.Select(i => i.Name));
                }
                WriteFieldBlock(sb, type.Fields);
                break;

            case "INTERFACE":
                sb.Append("interface ").Append(type.Name);
                WriteFieldBlock(sb, type.Fields);
                break;

            case "UNION":
                sb.Append("union ").Append(type.Name).Append(" = ");
                sb.AppendJoin(
                    " | ",
                    type.PossibleTypes?.Select(p => p.Name) ?? Enumerable.Empty<string?>()
                );
                sb.Append('\n');
                break;

            case "ENUM":
                sb.Append("enum ").Append(type.Name).Append(" {\n");
                if (type.EnumValues is not null)
                {
                    foreach (var ev in type.EnumValues)
                    {
                        sb.Append("  ").Append(ev.Name);
                        if (ev.IsDeprecated)
                            WriteDeprecation(sb, ev.DeprecationReason);
                        sb.Append('\n');
                    }
                }
                sb.Append("}\n");
                break;

            case "INPUT_OBJECT":
                sb.Append("input ").Append(type.Name).Append(" {\n");
                if (type.InputFields is not null)
                {
                    foreach (var f in type.InputFields)
                    {
                        sb.Append("  ").Append(f.Name).Append(": ").Append(WriteTypeRef(f.Type));
                        if (f.DefaultValue is not null)
                            sb.Append(" = ").Append(f.DefaultValue);
                        sb.Append('\n');
                    }
                }
                sb.Append("}\n");
                break;

            default:
                throw new GraphQLSchemaIntrospectionException(
                    $"Unrecognized introspection type kind '{type.Kind}' for type '{type.Name}'."
                );
        }
    }

    private static void WriteFieldBlock(StringBuilder sb, List<IntrospectionField>? fields)
    {
        sb.Append(" {\n");
        if (fields is not null)
        {
            foreach (var f in fields)
                WriteField(sb, f);
        }
        sb.Append("}\n");
    }

    private static void WriteField(StringBuilder sb, IntrospectionField field)
    {
        sb.Append("  ").Append(field.Name);

        if (field.Args is { Count: > 0 })
        {
            sb.Append('(');
            var first = true;
            foreach (var a in field.Args)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(a.Name).Append(": ").Append(WriteTypeRef(a.Type));
                if (a.DefaultValue is not null)
                    sb.Append(" = ").Append(a.DefaultValue);
            }
            sb.Append(')');
        }

        sb.Append(": ").Append(WriteTypeRef(field.Type));
        if (field.IsDeprecated)
            WriteDeprecation(sb, field.DeprecationReason);
        sb.Append('\n');
    }

    private static string WriteTypeRef(IntrospectionTypeRef typeRef)
    {
        return typeRef.Kind switch
        {
            "NON_NULL" => WriteTypeRef(
                typeRef.OfType
                    ?? throw new GraphQLSchemaIntrospectionException(
                        "NON_NULL type ref missing ofType."
                    )
            ) + "!",
            "LIST" => "["
                + WriteTypeRef(
                    typeRef.OfType
                        ?? throw new GraphQLSchemaIntrospectionException(
                            "LIST type ref missing ofType."
                        )
                )
                + "]",
            _ => typeRef.Name
                ?? throw new GraphQLSchemaIntrospectionException(
                    $"Named type ref of kind '{typeRef.Kind}' missing name."
                ),
        };
    }

    private static void WriteDeprecation(StringBuilder sb, string? reason)
    {
        sb.Append(" @deprecated");
        if (!string.IsNullOrEmpty(reason))
            sb.Append("(reason: \"").Append(EscapeString(reason)).Append("\")");
    }

    private static string EscapeString(string value)
    {
        // Minimal string escape for the subset we emit (deprecation reasons).
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
