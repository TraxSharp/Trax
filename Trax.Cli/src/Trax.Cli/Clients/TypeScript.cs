using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trax.Cli.Clients;

/// <summary>
/// How the client generator writes names, strings and comments into TypeScript. Everything that comes from the
/// manifest is data: a name is checked to be an identifier before it is written as one, a string is written as a JSON
/// literal, and a comment cannot close itself.
/// </summary>
internal static partial class TypeScript
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "break",
        "case",
        "catch",
        "class",
        "const",
        "continue",
        "debugger",
        "default",
        "delete",
        "do",
        "else",
        "enum",
        "export",
        "extends",
        "false",
        "finally",
        "for",
        "function",
        "if",
        "import",
        "in",
        "instanceof",
        "new",
        "null",
        "return",
        "super",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "var",
        "void",
        "while",
        "with",
        "as",
        "implements",
        "interface",
        "let",
        "package",
        "private",
        "protected",
        "public",
        "static",
        "yield",
        "any",
        "boolean",
        "number",
        "string",
        "symbol",
        "unknown",
        "never",
        "object",
        "undefined",
        "await",
        "async",
        "type",
        "of",
        "Array",
        "Promise",
        "eval",
        "arguments",
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.Default,
    };

    /// <summary>
    /// <paramref name="name"/>, refused unless it is an identifier TypeScript accepts where a declaration names one:
    /// ASCII letters, digits, <c>_</c> and <c>$</c>, not starting with a digit, and no reserved word.
    /// </summary>
    public static string Identifier(string name, string what)
    {
        if (!IsIdentifier(name) || Reserved.Contains(name))
            throw new InvalidOperationException(
                $"The {what} '{name}' cannot name generated TypeScript: it is not an identifier, or it is a word "
                    + "TypeScript reserves. Rename it on the host."
            );
        return name;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is ASCII letters, digits, <c>_</c> and <c>$</c>, not starting with a digit, and
    /// nothing after it: a trailing newline is not part of an identifier.
    /// </summary>
    public static bool IsIdentifier(string name) => IdentifierPattern().IsMatch(name);

    /// <summary>
    /// Whether <paramref name="name"/> is a member of <c>Object.prototype</c>. A state or trigger named so is found on
    /// every object a lookup reads it from, and a <c>__proto__</c> key in an object literal sets its prototype.
    /// </summary>
    public static bool IsPrototypeMember(string name) => PrototypeMembers.Contains(name);

    private static readonly HashSet<string> PrototypeMembers = new(StringComparer.Ordinal)
    {
        "__proto__",
        "__defineGetter__",
        "__defineSetter__",
        "__lookupGetter__",
        "__lookupSetter__",
        "constructor",
        "hasOwnProperty",
        "isPrototypeOf",
        "propertyIsEnumerable",
        "toLocaleString",
        "toString",
        "valueOf",
    };

    /// <summary>A string as a TypeScript literal: its JSON, so nothing in it can end the literal.</summary>
    public static string Literal(string value) => JsonSerializer.Serialize(value, Json);

    /// <summary>
    /// A doc comment holding <paramref name="text"/>, or nothing when it is empty. A <c>*/</c> in the text is broken,
    /// so the text cannot end the comment and become code.
    /// </summary>
    public static string Doc(string? text, string indent = "")
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var lines = text.Replace("\r\n", "\n").Replace("*/", "*\\/").Trim().Split('\n');
        if (lines.Length == 1)
            return $"{indent}/** {lines[0].Trim()} */\n";
        var sb = new StringBuilder().Append(indent).Append("/**\n");
        foreach (var line in lines)
            sb.Append(indent).Append(" * ").Append(line.TrimEnd()).Append('\n');
        return sb.Append(indent).Append(" */\n").ToString();
    }

    /// <summary>A kebab-case machine id in PascalCase: <c>topic-map</c> is <c>TopicMap</c>.</summary>
    public static string Pascal(string kebab) =>
        string.Concat(
            kebab
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );

    /// <summary>A kebab-case machine id in camelCase: <c>topic-map</c> is <c>topicMap</c>.</summary>
    public static string Camel(string kebab)
    {
        var pascal = Pascal(kebab);
        return pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>The first letter of a GraphQL name in upper case: <c>startRun</c> is <c>StartRun</c>.</summary>
    public static string Upper(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*\z")]
    private static partial Regex IdentifierPattern();
}
