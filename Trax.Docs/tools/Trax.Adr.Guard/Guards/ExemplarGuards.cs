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

            var claims = Claims(section);
            var unenforced = section.Contains(UnenforcedMarker, StringComparison.Ordinal);
            var elsewhere = section.Contains(ElsewhereMarker, StringComparison.Ordinal);

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
                var problem = Reasons.Problem(ReasonText(section, UnenforcedMarker));
                if (problem is not null)
                    offenders.Add($"{adr.RelativePath}: '{UnenforcedMarker}' {problem}");
            }

            if (elsewhere)
            {
                var problem = Reasons.Problem(ReasonText(section, ElsewhereMarker));
                if (problem is not null)
                    offenders.Add($"{adr.RelativePath}: '{ElsewhereMarker}' {problem}");
            }
        }

        return new GuardResult(
            "exemplars/section",
            offenders,
            adrs.Count,
            "Every ADR ends with '## Exemplars'. Name the guard tests that hold the decision up, "
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
                inspected++;
                if (!classes.ContainsKey(claim))
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
            adrs.Count == 0 ? 0 : Math.Max(inspected, 1),
            "A backticked bare class name in '## Exemplars' is an enforcement claim, and must "
                + "resolve to a real class in this repository's test roots."
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
                var source = File.ReadAllText(sourcePath);
                if (!source.Contains(adr.FileName, StringComparison.Ordinal))
                    offenders.Add(
                        $"{AdrCorpus.Relative(sourcePath, options)}: '{claim}' is named by "
                            + $"{adr.RelativePath} but does not cite it back. Name "
                            + $"'{adr.FileName}' in the class docstring AND in the assertion failure "
                            + "message, so the person who trips the guard sees the authority."
                    );
            }
        }

        return new GuardResult(
            "exemplars/guards-cite-back",
            offenders,
            adrs.Count == 0 ? 0 : Math.Max(inspected, 1),
            "A guard an ADR names must cite that ADR back, so a rewrite that guts its assertions "
                + "warns the person doing it. Follow NoSilentRegistrationOrderDependenceTests: the "
                + "citation goes in the <remarks> and in the failure message."
        );
    }

    /// <summary>
    /// Bare class names claimed as local enforcement. The "enforced elsewhere" paragraph is
    /// removed first: a class named there lives in another repo, so reading it as a local
    /// claim would demand it resolve here and fail every cross-repo ADR.
    /// </summary>
    private static List<string> Claims(string section) =>
        ClaimedGuard
            .Matches(WithoutParagraph(section, ElsewhereMarker))
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
    private static Dictionary<string, string> DeclaredClasses(GuardOptions options)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

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

                foreach (Match match in ClassDeclaration.Matches(File.ReadAllText(file)))
                    map.TryAdd(match.Groups["name"].Value, file);
            }
        }

        return map;
    }
}
