namespace Trax.Adr.Guard.Guards;

/// <summary>
/// The things that show a decision in force: the guards that hold it up, and the docs,
/// models and migrations that demonstrate it. This section is what lets an ADR stay
/// short, because anything it would otherwise have to explain goes here as a link.
/// </summary>
public static class ExemplarGuards
{
    public const string Heading = "Exemplars";

    /// <summary>Admits that nothing checks the decision, and says why nothing can.</summary>
    public const string UnenforcedMarker = "**Unenforced:**";

    /// <summary>
    /// Names what enforces the decision in another repository. The central corpus needs
    /// this: a decision binding eight repos is held up by guards in those repos, and this
    /// corpus cannot see them. Claiming "unenforced" would be a lie, and naming a bare
    /// class that does not resolve here would be a broken claim, so the third state says
    /// plainly that the enforcement is real and is not verified from here.
    /// </summary>
    public const string ElsewhereMarker = "**Enforced elsewhere:**";

    /// <summary>
    /// A claim the build can check: a bare backticked class name. Anything carrying a dot
    /// or a slash (<c>HygieneGuards.NoIgnoreAttribute</c>, <c>Trax.Docs/adr/0003-x.md</c>)
    /// names something in another repository and is read as prose, and a cited test FILE
    /// keeps its <c>.cs</c> extension so it is not mistaken for an enforcement claim.
    /// </summary>
    private static readonly Regex ClaimedGuard = new(
        @"`(?<name>[A-Z][A-Za-z0-9]*Tests)`",
        RegexOptions.Compiled
    );

    private static readonly Regex ClassDeclaration = new(
        @"\bclass\s+(?<name>\w+)\b",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The declaration an ADR's claim resolves against: an NUnit property carrying the ADR
    /// this class enforces, immediately above the class it applies to.
    ///
    /// <para>
    /// Matching a bare class name was ambiguous, and the ambiguity was live: Trax.Scheduler
    /// declares two classes called <c>HttpRunExecutorTests</c>, one pinning the outgoing
    /// request shape and one covering response mapping. The claim resolved against whichever
    /// the scan reached first, so deleting the one doing the work left the ADR green. A
    /// declaration names the ADR back, so there is nothing to guess.
    /// </para>
    /// </summary>
    private static readonly Regex GuardProperty = new(
        @"\[\s*Property\(\s*""adr""\s*,\s*""(?<adr>[^""]+)""\s*\)\s*\]",
        RegexOptions.Compiled
    );

    public static GuardResult SectionPresent(IReadOnlyList<Adr> adrs)
    {
        var offenders = new List<string>();

        foreach (var adr in adrs)
        {
            var section = adr.Section(Heading);
            if (section is null)
            {
                offenders.Add($"{adr.RelativePath}: no '## {Heading}' section");
                continue;
            }

            // Markers and claims are read from prose only, both here and in Claims below. A
            // fenced example quoting the template is not this document declaring anything, and
            // reading it as one let an ADR whose real Exemplars section was silent satisfy the
            // check.
            var prose = Markdown.WithoutFences(section);
            var claims = Claims(section);
            var unenforced = prose.Contains(UnenforcedMarker, StringComparison.Ordinal);
            var elsewhere = prose.Contains(ElsewhereMarker, StringComparison.Ordinal);

            if (unenforced && (claims.Count > 0 || elsewhere))
            {
                offenders.Add(
                    $"{adr.RelativePath}: declares '{UnenforcedMarker}' AND claims enforcement. It is "
                        + "one or the other; if something does enforce it, drop the marker."
                );
                continue;
            }

            if (claims.Count == 0 && !unenforced && !elsewhere)
            {
                offenders.Add(
                    $"{adr.RelativePath}: names no guard in this repo and declares neither "
                        + $"'{ElsewhereMarker}' nor '{UnenforcedMarker}'. Silence reads as a rule the "
                        + "build is holding for you."
                );
                continue;
            }

            if (unenforced)
            {
                var problem = Reasons.Problem(ReasonText(prose, UnenforcedMarker));
                if (problem is not null)
                    offenders.Add($"{adr.RelativePath}: '{UnenforcedMarker}' {problem}");
            }

            if (elsewhere)
            {
                var problem = Reasons.Problem(ReasonText(prose, ElsewhereMarker));
                if (problem is not null)
                    offenders.Add($"{adr.RelativePath}: '{ElsewhereMarker}' {problem}");
            }
        }

        return new GuardResult(
            "exemplars/section",
            offenders,
            adrs.Count,
            "Every ADR carries '## Exemplars'. Name the guard tests that hold the decision up, "
                + "or write '"
                + UnenforcedMarker
                + " <reason>' and say why nothing can. Both is a "
                + "contradiction; neither is silence, and silence is what is not allowed. Saying what "
                + "the guards do NOT cover is the half no test can verify and the half that stops a "
                + "reader over-trusting the link."
        );
    }

    /// <summary>
    /// A named guard must still exist. A rename breaks this loudly, which is the point:
    /// an ADR claiming enforcement that no longer exists is worse than one claiming none.
    /// </summary>
    public static GuardResult NamedGuardsResolve(IReadOnlyList<Adr> adrs, GuardOptions options)
    {
        var (classes, declared) = Scan(options);
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var adr in adrs)
        {
            var section = adr.Section(Heading);
            if (section is null)
                continue;

            foreach (var claim in Claims(section))
            {
                inspected++;

                var answering = declared
                    .Where(d =>
                        d.Name == claim && d.Adr.EndsWith(adr.FileName, StringComparison.Ordinal)
                    )
                    .ToList();

                if (answering.Count == 1)
                    continue;

                if (answering.Count > 1)
                {
                    offenders.Add(
                        $"{adr.RelativePath}: names '{claim}', and {answering.Count} classes claim "
                            + $"it back: {string.Join(", ", answering.Select(a => AdrCorpus.Relative(a.File, options)))}. "
                            + "A claim must resolve to one declaration; drop the attribute from the others."
                    );
                    continue;
                }

                if (classes.ContainsKey(claim))
                {
                    offenders.Add(
                        $"{adr.RelativePath}: names '{claim}', which exists but does not claim this ADR "
                            + $"back. Put [Property(\"adr\", \"...{adr.FileName}\")] on the class that "
                            + "enforces it, so the claim resolves to one declaration rather than to a name."
                    );
                    continue;
                }

                offenders.Add(
                    $"{adr.RelativePath}: names '{claim}', which is not a class under "
                        + $"{string.Join(" or ", options.TestRoots)}/. If it lives in another repo, "
                        + "cite it by qualified path so it reads as prose rather than a claim."
                );
            }
        }

        return new GuardResult(
            "exemplars/guards-resolve",
            offenders,
            inspected,
            "A backticked bare class name in '## Exemplars' is an enforcement claim. It resolves "
                + "to the class carrying [Property(\"adr\", \"...\")] for this ADR, not to whichever "
                + "class happens to share the name.",
            AllowsEmpty: true
        );
    }

    /// <summary>
    /// A guard an ADR leans on must say so, so the link survives the edit that matters.
    ///
    /// <para>
    /// Resolution only checks the class still EXISTS, which a rename breaks loudly and a
    /// rewrite does not. Someone gutting a guard's assertions sees nothing telling them an
    /// ADR depends on it, and the ADR goes on claiming enforcement that has quietly
    /// stopped. The back-citation puts that warning where the person editing the test is
    /// looking.
    /// </para>
    /// </summary>
    public static GuardResult NamedGuardsCiteBack(IReadOnlyList<Adr> adrs, GuardOptions options)
    {
        var classes = DeclaredClasses(options);
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var adr in adrs)
        {
            var section = adr.Section(Heading);
            if (section is null)
                continue;

            foreach (var claim in Claims(section))
            {
                if (!classes.TryGetValue(claim, out var sourcePath))
                    continue; // resolution owns this failure

                inspected++;
                var problem = CitationProblem(File.ReadAllText(sourcePath), adr.FileName);
                if (problem is not null)
                    offenders.Add(
                        $"{AdrCorpus.Relative(sourcePath, options)}: '{claim}' is named by "
                            + $"{adr.RelativePath} but {problem} Name "
                            + $"'{adr.FileName}' in the assertion failure message, so the person who "
                            + "trips the guard sees the authority. The attribute establishes the link; "
                            + "the message is what they read when it goes red."
                    );
            }
        }

        return new GuardResult(
            "exemplars/guards-cite-back",
            offenders,
            inspected,
            "A guard an ADR names must cite that ADR back, so a rewrite that guts its assertions "
                + "warns the person doing it. Follow NoSilentRegistrationOrderDependenceTests: the "
                + "citation goes in the class docstring and in the assertion failure message.",
            AllowsEmpty: true
        );
    }

    /// <summary>
    /// Null when the source cites the ADR the way the standard asks: once in a documentation
    /// comment, and once outside one, which in practice is the assertion failure message.
    ///
    /// <para>
    /// Checking only that the name appeared somewhere was too weak to mean anything. A stray
    /// comment or an unrelated string literal satisfied it, while the message the guard
    /// prints told the reader to put it in two specific places.
    /// </para>
    /// </summary>
    private static string? CitationProblem(string source, string fileName)
    {
        var inDoc = false;
        var inCode = false;

        foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
        {
            if (!line.Contains(fileName, StringComparison.Ordinal))
                continue;

            // The attribute names the ADR too, and it is Code. Letting it answer here would
            // mean tagging a class also satisfied the message requirement, which is the half
            // the person who trips the guard actually reads.
            if (GuardProperty.IsMatch(line))
                continue;

            switch (CSharp.Classify(line))
            {
                case CSharp.LineKind.Documentation:
                    inDoc = true;
                    break;
                case CSharp.LineKind.Code:
                    inCode = true;
                    break;
                // An ordinary // comment is neither. The standard asks for the docstring and
                // the failure message, and a comment is not the message anybody reads when
                // the guard goes red.
            }
        }

        return (inDoc, inCode) switch
        {
            (_, true) => null,
            (_, false) =>
                "does not name it in a failure message, only in the attribute or the docstring.",
        };
    }

    /// <summary>
    /// Bare class names claimed as local enforcement. Fenced examples go first, then the
    /// "enforced elsewhere" paragraph: a class named there is not a claim on this repo, so
    /// reading it as one would demand it resolve here and fail every cross-repo ADR.
    ///
    /// <para>
    /// The order is the whole of it. Stripping the paragraph from the raw section meant a
    /// fenced example quoting the marker took the prose below it as well, and a real claim
    /// disappeared: resolution and cite-back both reported nothing to check instead of failing.
    /// </para>
    ///
    /// <para>
    /// Public because the census must ask the same question. When it asked a slightly
    /// different one, moving a local class's name into the "enforced elsewhere" paragraph
    /// credited it to the census while hiding it from resolution and cite-back at once.
    /// </para>
    /// </summary>
    public static List<string> Claims(string section) =>
        ClaimedGuard
            .Matches(WithoutParagraph(Markdown.WithoutFences(section), ElsewhereMarker))
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Drops the paragraph a marker opens, up to the next blank line.</summary>
    private static string WithoutParagraph(string section, string marker)
    {
        var index = section.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return section;

        var rest = section[index..];
        var stop = rest.IndexOf("\n\n", StringComparison.Ordinal);
        return stop < 0 ? section[..index] : section[..index] + rest[(stop + 2)..];
    }

    private static string ReasonText(string section, string marker)
    {
        var index = section.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return string.Empty;

        var after = section[(index + marker.Length)..];
        var stop = after.IndexOf("\n\n", StringComparison.Ordinal);
        if (stop >= 0)
            after = after[..stop];

        return Regex.Replace(after, @"\s+", " ").Trim();
    }

    /// <summary>Class name to the file declaring it, for every .cs file under the test roots.</summary>
    private static Dictionary<string, string> DeclaredClasses(GuardOptions options) =>
        Scan(options).Classes;

    /// <summary>Every class an ADR could claim, and every class that claims an ADR back.</summary>
    private static (
        Dictionary<string, string> Classes,
        List<(string Name, string Adr, string File)> Declared
    ) Scan(GuardOptions options)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var declared = new List<(string, string, string)>();

        foreach (var root in options.TestRoots)
        {
            var dir = Path.Combine(
                options.RepoRoot,
                root.Replace('/', Path.DirectorySeparatorChar)
            );
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var relative = AdrCorpus.Relative(file, options);
                if (relative.Split('/').Any(p => p is "bin" or "obj"))
                    continue;

                var raw = File.ReadAllText(file);
                foreach (
                    Match match in ClassDeclaration.Matches(CSharp.WithoutCommentsAndLiterals(raw))
                )
                    map.TryAdd(match.Groups["name"].Value, file);

                // Line-based, because the attribute's value is a string literal: it does not
                // survive the blanking pass the class scan uses. Classify is what keeps a
                // commented-out attribute from counting.
                string? pending = null;
                foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
                {
                    if (CSharp.Classify(line) != CSharp.LineKind.Code)
                        continue;

                    var property = GuardProperty.Match(line);
                    if (property.Success)
                    {
                        pending = property.Groups["adr"].Value;
                        continue;
                    }

                    var declaration = ClassDeclaration.Match(line);
                    if (declaration.Success)
                    {
                        if (pending is not null)
                            declared.Add((declaration.Groups["name"].Value, pending, file));
                        pending = null;
                        continue;
                    }

                    // Anything else between the attribute and a class ends the pairing, so a
                    // class declaration quoted in a fixture string cannot inherit it.
                    if (!line.TrimStart().StartsWith('[') && line.Trim().Length > 0)
                        pending = null;
                }
            }
        }

        return (map, declared);
    }
}
