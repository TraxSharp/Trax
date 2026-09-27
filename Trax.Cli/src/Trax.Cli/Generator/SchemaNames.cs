using System.Text.RegularExpressions;
using Trax.Cli.Models;

namespace Trax.Cli.Generator;

/// <summary>
/// Checks every name a schema contributes to the generated project before anything is written.
/// Names become identifiers, namespaces, type references and file paths, so each one has to match
/// <c>[A-Za-z_][A-Za-z0-9_]*</c> (a project name may be several of those joined by dots, and a
/// field type may be a generic or array expression built from them). A schema with any name that
/// does not is refused as a whole, with every offending name listed, rather than rewritten: a
/// rewritten name changes the generated contract and can collide with another one, while a
/// refusal leaves the author to rename it in the schema.
/// </summary>
internal static partial class SchemaNames
{
    internal const string Pattern = "[A-Za-z_][A-Za-z0-9_]*";

    internal static bool IsIdentifier(string? name) =>
        name is not null && Identifier().IsMatch(name);

    internal static void Validate(ApiSchema schema, string projectName)
    {
        var problems = new List<string>();

        if (!IsDottedIdentifier(projectName))
            problems.Add($"project name '{projectName}'");

        foreach (var type in schema.Types)
            CheckType(type, "type", problems);

        foreach (var apiEnum in schema.Enums)
        {
            Check(apiEnum.Name, "enum", problems);
            foreach (var value in apiEnum.Values)
                Check(value, $"enum value of '{apiEnum.Name}'", problems);
        }

        foreach (var operation in schema.Operations)
        {
            Check(operation.Name, "operation", problems);
            if (operation.Group is not null)
                Check(operation.Group, $"group of operation '{operation.Name}'", problems);
            CheckType(operation.InputType, $"input type of '{operation.Name}'", problems);
            CheckType(operation.OutputType, $"output type of '{operation.Name}'", problems);
        }

        if (problems.Count == 0)
            return;

        throw new InvalidOperationException(
            "The schema has names that cannot be used in generated C#. Every name has to match "
                + Pattern
                + " (after the generator's PascalCase conversion); rename these in the schema:"
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
            if (!IsTypeExpression(field.TypeName))
                problems.Add(
                    $"type of property '{field.Name}' of '{type.Name}': '{field.TypeName}'"
                );
        }
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
