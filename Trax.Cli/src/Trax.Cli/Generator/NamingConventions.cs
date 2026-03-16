using System.Text;
using System.Text.RegularExpressions;

namespace Trax.Cli.Generator;

public static partial class NamingConventions
{
    private static readonly HashSet<string> CSharpKeywords = new()
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    };

    private static readonly HashSet<string> VerbPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "create",
        "get",
        "update",
        "delete",
        "list",
        "fetch",
        "lookup",
        "find",
        "search",
        "remove",
        "add",
        "set",
        "put",
        "patch",
        "post",
    };

    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Split on separators: _, -, spaces, and camelCase boundaries
        var parts = SplitPattern().Split(name).Where(p => p.Length > 0).ToList();

        if (parts.Count == 0)
            return name;

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0)
                continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                sb.Append(part[1..].ToLowerInvariant());
        }

        return sb.ToString();
    }

    public static string ToCamelCase(string name)
    {
        var pascal = ToPascalCase(name);
        if (pascal.Length == 0)
            return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    public static string SanitizeIdentifier(string name)
    {
        if (CSharpKeywords.Contains(name))
            return $"@{name}";
        return name;
    }

    public static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var parts = SplitPattern().Split(name).Where(p => p.Length > 0).ToList();

        return string.Join("-", parts.Select(p => p.ToLowerInvariant()));
    }

    public static string DeriveGroupName(string operationName)
    {
        var pascal = ToPascalCase(operationName);

        // Try to extract the noun by removing known verb prefixes
        foreach (var verb in VerbPrefixes)
        {
            var verbPascal = ToPascalCase(verb);
            if (
                pascal.StartsWith(verbPascal, StringComparison.Ordinal)
                && pascal.Length > verbPascal.Length
            )
            {
                var noun = pascal[verbPascal.Length..];
                return Pluralize(noun);
            }
        }

        // No recognized verb prefix — use the full name pluralized
        return Pluralize(pascal);
    }

    private static string Pluralize(string noun)
    {
        if (noun.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return noun;
        if (noun.EndsWith("y", StringComparison.OrdinalIgnoreCase) && noun.Length > 1)
        {
            var beforeY = noun[^2];
            if (beforeY is not ('a' or 'e' or 'i' or 'o' or 'u'))
                return noun[..^1] + "ies";
        }
        return noun + "s";
    }

    [GeneratedRegex(@"[_\-\s]+|(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex SplitPattern();
}
