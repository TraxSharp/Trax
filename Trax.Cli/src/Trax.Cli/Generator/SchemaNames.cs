using System.Text.RegularExpressions;
using Trax.Cli.Models;

namespace Trax.Cli.Generator;

/// <summary>
/// Checks every name a schema contributes to the generated project before anything is written.
/// Names become identifiers, namespaces, type references and file paths, so each one has to match
/// <c>[A-Za-z_][A-Za-z0-9_]*</c> (a project name may be several of those joined by dots, and a
/// field type may be a generic or array expression built from them), and no two properties of one
/// type or values of one enum may be the same name after conversion. A schema with any name that
/// does not is refused as a whole, with every offending name listed, rather than rewritten: a
/// rewritten name changes the generated contract and can collide with another one, while a
/// refusal leaves the author to rename it in the schema.
/// </summary>
internal static partial class SchemaNames
{
    internal const string Pattern = "[A-Za-z_][A-Za-z0-9_]*";

    /// <summary>
    /// Type names the parsers write for something other than a model: <c>Guid</c>, <c>DateTime</c>,
    /// <c>DateOnly</c> and <c>Uri</c> for formatted strings, and <c>Unit</c> for an operation that returns
    /// nothing. A model of one of these names would be read as that model wherever the parsers meant
    /// the framework type, so it is refused like any other name the generator cannot emit.
    /// </summary>
    internal static readonly IReadOnlySet<string> Reserved = new HashSet<string>(
        ["Unit", "Guid", "DateTime", "DateOnly", "Uri"],
        StringComparer.Ordinal
    );

    internal static bool IsIdentifier(string? name) =>
        name is not null && Identifier().IsMatch(name);

    internal static void Validate(ApiSchema schema, string projectName)
    {
        var problems = new List<string>();

        if (!IsDottedIdentifier(projectName))
            problems.Add($"project name '{projectName}'");

        foreach (var type in schema.Types)
            CheckType(type, "type", problems);

        foreach (
            var name in schema
                .Types.Where(t => !t.IsBuiltIn)
                .Select(t => t.Name)
                .Concat(schema.Enums.Select(e => e.Name))
                .Where(Reserved.Contains)
                .Distinct()
        )
            problems.Add($"type '{name}' (reserved for the framework type of that name)");

        foreach (var apiEnum in schema.Enums)
        {
            Check(apiEnum.Name, "enum", problems);
            foreach (var value in apiEnum.Values)
                Check(value, $"enum value of '{apiEnum.Name}'", problems);
            CheckUnique(apiEnum.Values, $"enum value of '{apiEnum.Name}'", problems);
        }

        foreach (var operation in schema.Operations)
        {
            Check(operation.Name, "operation", problems);
            if (operation.Group is not null)
                Check(operation.Group, $"group of operation '{operation.Name}'", problems);
            CheckType(operation.InputType, $"input type of '{operation.Name}'", problems);
            CheckType(operation.OutputType, $"output type of '{operation.Name}'", problems);
        }

        CheckDistinct(ModelSources(schema), "type or enum", problems);
        CheckDistinct(
            schema.Operations.Select(o => (o.Name, o.SourceName ?? o.Name)),
            "operation",
            problems
        );
        CheckDistinct(
            schema
                .Operations.Select(o => o.Group)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(g => (g, g)),
            "group",
            problems
        );

        if (problems.Count == 0)
            return;

        throw new InvalidOperationException(
            "The schema has names that cannot be used in generated C#. Every name has to match "
                + Pattern
                + " (after the generator's PascalCase conversion), and no two may convert to the same"
                + " one where they share a scope: the properties of a type, the values of an enum,"
                + " and, ignoring case because each becomes a file or folder name, the types and enums,"
                + " the operations, and the groups. Rename these in the schema:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    problems.Distinct().Select(p => "  " + GeneratedText.SingleLine(p))
                )
        );
    }

    private static void CheckType(ApiType type, string kind, List<string> problems)
    {
        Check(type.Name, kind, problems);
        foreach (var field in type.Fields)
        {
            Check(field.Name, $"property of '{type.Name}'", problems);
            // C# forbids a member named like its enclosing type (CS0542).
            if (string.Equals(field.Name, type.Name, StringComparison.Ordinal))
                problems.Add(
                    $"property '{field.Name}' of '{type.Name}' (a property cannot have its type's name)"
                );
            if (!IsTypeExpression(field.TypeName))
                problems.Add(
                    $"type of property '{field.Name}' of '{type.Name}': '{field.TypeName}'"
                );
        }
        CheckUnique(type.Fields.Select(f => f.Name), $"property of '{type.Name}'", problems);
    }

    /// <summary>
    /// Two schema names that the PascalCase conversion turns into one (<c>first-name</c> and
    /// <c>firstName</c>) would declare the same member twice. The schema is refused rather than
    /// one of them renamed or dropped, for the same reason a name outside the pattern is.
    /// </summary>
    private static void CheckUnique(IEnumerable<string> names, string kind, List<string> problems)
    {
        foreach (
            var name in names
                .GroupBy(n => n, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
        )
            problems.Add($"{kind} '{name}' (more than once after the PascalCase conversion)");
    }

    /// <summary>
    /// Every type and enum written to <c>Models/</c>, each a distinct schema definition. The parsers
    /// share one instance between the places a type is used, so the same instance twice is one type.
    /// </summary>
    private static IEnumerable<(string Name, string Source)> ModelSources(ApiSchema schema) =>
        schema
            .Types.Where(t => !t.IsBuiltIn)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<ApiType>()
            .Select(t => (t.Name, t.SourceName ?? t.Name))
            .Concat(
                schema
                    .Enums.Distinct(ReferenceEqualityComparer.Instance)
                    .Cast<ApiEnum>()
                    .Select(e => (e.Name, e.SourceName ?? e.Name))
            );

    /// <summary>
    /// Distinct schema definitions that become one C# name, or names differing only in case, which
    /// are one file or folder on a case-insensitive file system. Each would silently merge with or
    /// overwrite the other, so the schema is refused, naming every definition involved.
    /// </summary>
    private static void CheckDistinct(
        IEnumerable<(string Name, string Source)> definitions,
        string kind,
        List<string> problems
    )
    {
        foreach (
            var group in definitions
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
        )
            problems.Add(
                $"{kind} '{group.Key}' comes from more than one definition: "
                    + string.Join(
                        ", ",
                        group.Select(d =>
                            d.Source == d.Name ? $"'{d.Source}'" : $"'{d.Source}' (as '{d.Name}')"
                        )
                    )
            );
    }

    private static void Check(string name, string kind, List<string> problems)
    {
        if (!IsIdentifier(name))
            problems.Add($"{kind} '{name}'");
    }

    internal static bool IsDottedIdentifier(string name) =>
        name.Length > 0 && name.Split('.').All(IsIdentifier);

    /// <summary>
    /// A type reference the parsers produce: <c>Name</c>, <c>Name&lt;T, U&gt;</c>, with an
    /// optional trailing <c>[]</c> and <c>?</c>.
    /// </summary>
    internal static bool IsTypeExpression(string text)
    {
        var position = 0;
        return ReadType(text, ref position) && position == text.Length;
    }

    private static bool ReadType(string text, ref int position)
    {
        var match = IdentifierAt().Match(text, position);
        if (!match.Success)
            return false;
        position += match.Length;

        if (position < text.Length && text[position] == '<')
        {
            position++;
            while (true)
            {
                if (!ReadType(text, ref position))
                    return false;
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    if (position < text.Length && text[position] == ' ')
                        position++;
                    continue;
                }
                if (position < text.Length && text[position] == '>')
                {
                    position++;
                    break;
                }
                return false;
            }
        }

        if (text.AsSpan(position).StartsWith("[]"))
            position += 2;
        if (position < text.Length && text[position] == '?')
            position++;
        return true;
    }

    [GeneratedRegex("^" + Pattern + @"\z")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"\G" + Pattern)]
    private static partial Regex IdentifierAt();
}
