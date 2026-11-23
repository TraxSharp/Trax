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
public static partial class CensusGuards
{
    public const string OptOutMarker = "Not ADR-enforcing:";

    [GeneratedRegex(
        @"\bclass\s+(?<name>[A-Za-z0-9]*Tests)\b",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ClassDeclaration { get; }

    /// <summary>
    /// A citation of any ADR, local or central: <c>docs/adr/0003-x.md</c> or
    /// <c>Trax.Docs/adr/0003-x.md</c>. The <c>central</c> group marks the second.
    /// </summary>
    [GeneratedRegex(
        @"(?<central>Trax\.Docs/)?adr/(?<file>\d{4}-[a-z0-9-]+\.md)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex AdrCitation { get; }

    public static GuardResult EveryGuardIsClassified(IReadOnlyList<Adr> adrs, GuardOptions options)
    {
        var rule =
            "Every guard class is either named by an ADR's '## Exemplars' section, or itself "
            + $"cites one itself, or declares '{OptOutMarker} <reason>' in its own docstring. A new "
            + "guard file is unclassified until you choose, and the reason has to be a reason: a "
            + "rule that should be recorded and is not yet should be recorded, not exempted.";

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

        // Each claim with the ADR making it, because a claim is answered by the class that
        // names that ADR back, not by every class sharing the claimed name.
        var claims = adrs.SelectMany(a =>
                a.Section(ExemplarGuards.Heading) is { } section
                    ? ExemplarGuards.Claims(section).Select(c => (Name: c, a.FileName))
                    : Enumerable.Empty<(string Name, string FileName)>()
            )
            .ToList();

        var local = adrs.Select(a => a.FileName).ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();
        var classified = 0;

        var files = Directory
            .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !AdrCorpus.Relative(f, options).Split('/').Any(p => p is "bin" or "obj"))
            .OrderBy(f => AdrCorpus.Relative(f, options), StringComparer.Ordinal);

        foreach (var file in files)
            classified += ClassifyFile(
                AdrCorpus.Relative(file, options),
                File.ReadAllText(file),
                claims,
                local,
                options,
                offenders
            );

        return new GuardResult("census/classified", offenders, classified, rule);
    }

    /// <summary>
    /// Classifies every guard class in one file, adding its offenders, and returns how many it
    /// classified.
    /// </summary>
    private static int ClassifyFile(
        string path,
        string source,
        List<(string Name, string FileName)> claims,
        HashSet<string> local,
        GuardOptions options,
        List<string> offenders
    )
    {
        var classified = 0;
        var declarations = ExemplarGuards.Declarations(source);

        foreach (var (guard, docstring) in GuardClasses(source))
        {
            // A guard is credited either because a local ADR names it, or because it names
            // an ADR itself. The second case is what makes the census usable in a repo whose
            // shared guards enforce decisions recorded in the central corpus: that corpus is
            // not present here, so nothing local can name them.
            //
            // A claim credits only the class that answers it: the one in this file carrying
            // the claiming ADR's attribute. Crediting by bare name let a second class sharing
            // the name ride on the first's claim, neither named by a decision nor opted out.
            var answersAClaim = claims.Exists(c =>
                string.Equals(c.Name, guard, StringComparison.Ordinal)
                && declarations.Any(d =>
                    string.Equals(d.Name, guard, StringComparison.Ordinal)
                    && d.Adr.EndsWith(c.FileName, StringComparison.Ordinal)
                )
            );
            var credited = answersAClaim || CitesAnAdr(docstring, local, options);
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

        return classified;
    }

    /// <summary>
    /// Every <c>*Tests</c> class a file declares, paired with the documentation comment
    /// immediately above it.
    ///
    /// <para>
    /// Declarations are found in the blanked source, so a class inside a comment or a literal
    /// is not counted, and the match runs over the whole text rather than line by line, so a
    /// declaration split across lines is seen here exactly as the exemplar checks see it. The
    /// two disagreeing let an uncredited guard escape the census while still satisfying a claim.
    /// </para>
    ///
    /// <para>
    /// The docstring is read from the same scan with comments kept, not from the raw file. A
    /// <c>///</c> line quoted inside a string literal is text, and reading the raw file let one
    /// planted in an attribute argument sit directly above a class and opt it out.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Name, string Docstring)> GuardClasses(string source)
    {
        var lines = CSharp
            .WithoutLiterals(source)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var blanked = CSharp
            .WithoutCommentsAndLiterals(source)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (Match match in ClassDeclaration.Matches(blanked))
        {
            // The line the class NAME sits on, which is where the docstring search starts.
            var line = blanked.Take(match.Groups["name"].Index).Count(c => c == '\n');
            yield return (match.Groups["name"].Value, DocstringAbove(lines, line));
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

            // The last line of an attribute csharpier wrapped one argument per line: step over
            // the whole attribute to the line that opens it. Stopping here lost the docstring
            // of any class whose attribute grew long enough to wrap.
            if (trimmed.TrimEnd().EndsWith(']'))
            {
                var opening = AttributeOpening(lines, i);
                if (opening >= 0)
                {
                    i = opening;
                    continue;
                }
            }

            break;
        }

        return string.Join('\n', doc);
    }

    /// <summary>
    /// The line opening the attribute that ends on <paramref name="closing"/>, or -1 when no
    /// line within <see cref="ExemplarGuards.MaxAttributeLines"/> opens one. Bounded like the
    /// exemplar scan's join, so a stray bracket cannot walk the search up the whole file.
    /// </summary>
    private static int AttributeOpening(string[] lines, int closing)
    {
        for (var j = closing - 1; j >= 0 && closing - j < ExemplarGuards.MaxAttributeLines; j--)
        {
            var trimmed = lines[j].TrimStart();
            if (trimmed.StartsWith('['))
                return j;

            // A docstring or a statement ends the search: an attribute's lines are neither.
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.EndsWith(';'))
                return -1;
        }

        return -1;
    }

    /// <summary>
    /// Whether a docstring cites an ADR that can be credited.
    ///
    /// <para>
    /// A local citation (<c>docs/adr/0003-x.md</c>) must name an ADR in this corpus. Any
    /// well-formed path used to count, so a guard citing a file that was renumbered, renamed
    /// or never written stayed credited to a decision nobody can read. A central citation
    /// (<c>Trax.Docs/adr/...</c>) cannot be checked from a consumer repo, whose checkout holds
    /// no central corpus, so it is taken as written there; in the central corpus itself it is
    /// local and is checked like one.
    /// </para>
    /// </summary>
    private static bool CitesAnAdr(
        string docstring,
        IReadOnlySet<string> local,
        GuardOptions options
    ) =>
        AdrCitation
            .Matches(docstring)
            .Any(m =>
                local.Contains(m.Groups["file"].Value)
                || (m.Groups["central"].Success && !options.RequireReposKey)
            );

    private static string? OptOutReason(string docstring)
    {
        var index = docstring.IndexOf(OptOutMarker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var after = docstring[(index + OptOutMarker.Length)..];
        var stop = after.IndexOf("</para>", StringComparison.Ordinal);
        if (stop >= 0)
            after = after[..stop];

        var cleaned = DocCommentMarkup.Replace(after, " ");
        cleaned = Whitespace.Replace(cleaned, " ").Trim();
        return cleaned.Length > 300 ? cleaned[..300] : cleaned;
    }

    [GeneratedRegex(@"///|<[^>]+>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DocCommentMarkup { get; }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace { get; }
}
