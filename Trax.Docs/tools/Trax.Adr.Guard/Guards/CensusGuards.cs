namespace Trax.Adr.Guard.Guards;

/// <summary>
/// The reverse direction: every guard class must be credited to an ADR, or say why the
/// rule it pins is not a recorded decision.
///
/// <para>
/// A guard nobody linked to a decision is the unaccounted-for instance. It pins something,
/// and no reader can tell whether that something was ever chosen deliberately or is an
/// accident somebody froze. Opting out is a normal answer, and a census forces the question
/// to be answered rather than left open.
/// </para>
/// </summary>
public static class CensusGuards
{
    public const string OptOutMarker = "Not ADR-enforcing:";

    private static readonly Regex ClassDeclaration = new(
        @"\bclass\s+(?<name>[A-Za-z0-9]*Tests)\b",
        RegexOptions.Compiled
    );

    public static GuardResult EveryGuardIsClassified(IReadOnlyList<Adr> adrs, GuardOptions options)
    {
        var rule =
            "Every guard class is either named by an ADR's '## Exemplars' section or declares "
            + $"'{OptOutMarker} <reason>' in its own docstring. A new guard file is unclassified "
            + "until you choose, and the reason has to be a reason: a rule that should be recorded "
            + "and is not yet should be recorded, not exempted.";

        if (options.CensusRoot is null)
            return new GuardResult("census/classified", [], 0, rule, AllowsEmpty: true);

        var dir = Path.Combine(
            options.RepoRoot,
            options.CensusRoot.Replace('/', Path.DirectorySeparatorChar)
        );

        if (!Directory.Exists(dir))
            return new GuardResult(
                "census/classified",
                [$"census root '{options.CensusRoot}' does not exist"],
                0,
                rule
            );

        var claimed = adrs.Select(a => a.Section(ExemplarGuards.Heading))
            .Where(s => s is not null)
            .SelectMany(s => ExemplarGuards.Claims(s!))
            .ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();
        var classified = 0;

        var files = Directory
            .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !AdrCorpus.Relative(f, options).Split('/').Any(p => p is "bin" or "obj"))
            .OrderBy(f => AdrCorpus.Relative(f, options), StringComparer.Ordinal);

        foreach (var file in files)
        {
            var path = AdrCorpus.Relative(file, options);

            foreach (var (guard, docstring) in GuardClasses(File.ReadAllText(file)))
            {
                var credited = claimed.Contains(guard);
                var reason = OptOutReason(docstring);

                if (credited && reason is not null)
                {
                    offenders.Add(
                        $"{path}: '{guard}' is named by an ADR AND declares '{OptOutMarker}'. It is "
                            + "one or the other; if it enforces the ADR, drop the marker."
                    );
                    continue;
                }

                if (!credited && reason is null)
                {
                    offenders.Add(
                        $"{path}: '{guard}' is named by no ADR and does not opt out. Add it to an "
                            + $"ADR's '## {ExemplarGuards.Heading}' section, or write '{OptOutMarker} "
                            + "<reason>' in its docstring."
                    );
                    continue;
                }

                var problem = reason is null ? null : Reasons.Problem(reason);
                if (problem is not null)
                    offenders.Add($"{path}: '{guard}' {problem}");

                classified++;
            }
        }

        return new GuardResult("census/classified", offenders, classified, rule);
    }

    /// <summary>
    /// Every <c>*Tests</c> class a file declares, paired with the documentation comment
    /// immediately above it.
    ///
    /// <para>
    /// Two defects are avoided here. Taking only the first match censused one class per
    /// file, so a second guard in the same file was never asked the question. And searching
    /// the whole file for the opt-out marker read it out of string literals: a fixture
    /// holding a template of a guard class was credited with the template's placeholder as
    /// its reason.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Name, string Docstring)> GuardClasses(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var inRawString = false;

        for (var i = 0; i < lines.Length; i++)
        {
            // Raw string literals carry fixture source in this project's own tests. What is
            // inside one is data, not a declaration.
            if (lines[i].Contains("\"\"\"", StringComparison.Ordinal))
            {
                inRawString = !inRawString;
                continue;
            }

            if (inRawString)
                continue;

            var match = ClassDeclaration.Match(lines[i]);
            if (match.Success)
                yield return (match.Groups["name"].Value, DocstringAbove(lines, i));
        }
    }

    /// <summary>The contiguous <c>///</c> block above a declaration, attributes skipped.</summary>
    private static string DocstringAbove(string[] lines, int declaration)
    {
        var doc = new List<string>();

        for (var i = declaration - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                doc.Insert(0, trimmed);
                continue;
            }

            // Attributes and blank lines sit between a docstring and its class.
            if (trimmed.Length == 0 || trimmed.StartsWith('['))
                continue;

            break;
        }

        return string.Join("\n", doc);
    }

    private static string? OptOutReason(string docstring)
    {
        var index = docstring.IndexOf(OptOutMarker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var after = docstring[(index + OptOutMarker.Length)..];
        var stop = after.IndexOf("</para>", StringComparison.Ordinal);
        if (stop >= 0)
            after = after[..stop];

        var cleaned = Regex.Replace(after, @"///|<[^>]+>", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length > 300 ? cleaned[..300] : cleaned;
    }
}
