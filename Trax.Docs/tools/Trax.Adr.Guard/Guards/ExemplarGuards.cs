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
    /// <para>
    /// A class may carry several, one per decision it holds up, either as separate attributes or
    /// in one list (<c>[Property("adr", "a"), Property("adr", "b")]</c>). Only an attribute line
    /// is read for them, so a string that merely mentions one is not a declaration.
    /// </para>
    private static readonly Regex GuardProperty = new(
        @"\bProperty\(\s*""adr""\s*,\s*""(?<adr>[^""]+)""\s*\)",
        RegexOptions.Compiled
    );

    /// <summary>Whether a line of code is an attribute naming an ADR.</summary>
    private static bool IsAdrAttribute(string line) =>
        line.TrimStart().StartsWith('[') && GuardProperty.IsMatch(line);

    /// <summary>How many lines an attribute may span before the scan stops joining them.</summary>
    internal const int MaxAttributeLines = 20;

    /// <summary>Whether every <c>[</c> in an attribute's text so far has been closed.</summary>
    private static bool Closed(string attribute) =>
        attribute.Count(c => c == '[') <= attribute.Count(c => c == ']');

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
                {
                    var idle = WhyItRunsNothing(File.ReadAllText(answering[0].File), claim);
                    if (idle is not null)
                        offenders.Add(
                            $"{AdrCorpus.Relative(answering[0].File, options)}: '{claim}' is named by "
                                + $"{adr.RelativePath} but {idle} A guard that never runs holds "
                                + "nothing up, so the claim would be enforcement in name only."
                        );
                    continue;
                }

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
                // The class that claims this ADR back, the same one resolution checks. Looking the
                // claim up by bare name instead read whichever same-named class the scan met first,
                // so the class doing the work could be left uncited while another answered for it.
                var answering = declared
                    .Where(d =>
                        d.Name == claim && d.Adr.EndsWith(adr.FileName, StringComparison.Ordinal)
                    )
                    .ToList();
                // With no class claiming it back, resolution reports the missing attribute and this
                // still reads the class by that name, so a missing failure message is reported too.
                // Two claiming classes is resolution's failure alone.
                string sourcePath;
                if (answering.Count == 1)
                    sourcePath = answering[0].File;
                else if (answering.Count == 0 && classes.TryGetValue(claim, out var named))
                    sourcePath = named;
                else
                    continue;

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
    /// Null when the source names the ADR in what an assertion prints on failure: an argument
    /// to an assertion, or a value one uses (the <c>private const string Adr = "..."</c> a
    /// message interpolates, or a <c>because</c> built from it).
    ///
    /// <para>
    /// Checking only that the name appeared on a code line was too weak to mean anything. A
    /// constant nothing reads satisfied it, so a class with no assertions at all cited its ADR
    /// "back" while the person who trips the guard would never see the authority.
    /// </para>
    /// </summary>
    private static string? CitationProblem(string source, string fileName)
    {
        var text = source.Replace("\r\n", "\n");
        var code = CSharp.WithoutCommentsAndLiterals(text);
        var literals = CSharp.WithoutLiterals(text);
        var followed = new HashSet<string>(StringComparer.Ordinal);

        // A citation is text inside a string literal. In a comment or a docstring it is not
        // what an assertion prints, and the attribute is never an assertion either.
        var citations = new List<int>();
        for (
            var at = text.IndexOf(fileName, StringComparison.Ordinal);
            at >= 0;
            at = text.IndexOf(fileName, at + 1, StringComparison.Ordinal)
        )
        {
            if (literals[at] == ' ')
                citations.Add(at);
        }

        bool ReachesAnAssertion(int position, int depth)
        {
            var statement = Statement(code, position);
            if (Assertion.IsMatch(statement))
                return true;

            // A value holding the citation counts when an assertion uses it. Followed a few
            // steps (a constant, then a 'because' built from it) and no further.
            var name = DeclaredName(statement);
            if (name is null || depth >= 3 || !followed.Add(name))
                return false;

            return References(text, code, literals, name)
                .Any(r => ReachesAnAssertion(r, depth + 1));
        }

        return citations.Any(c => ReachesAnAssertion(c, 0))
            ? null
            : "does not name it in a failure message: not in an assertion's arguments, nor in a "
                + "value an assertion uses. The attribute, a comment and the docstring do not count.";
    }

    /// <summary>A call that asserts: NUnit, a fluent assertion library, or a helper named for it.</summary>
    private static readonly Regex Assertion = new(
        @"\b\w*Assert\w*\s*\.\s*\w+\s*[<(]|\bAssert\w*\s*\(|\.\s*Should\w*\s*\(|\bFailWith\s*\("
            + @"|\bAssertionScope\b|\bthrow\s+new\s+\w*Assertion\w*Exception\b",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The member or local a statement declares: <c>Name(...) =&gt;</c>, <c>Name =&gt;</c> or
    /// <c>Name = ...</c>. Read from blanked code, so nothing inside a literal is mistaken for one.
    /// </summary>
    private static readonly Regex Declaration = new(
        @"(?<name>[A-Za-z_]\w*)\s*(?:\([^()]*\)\s*)?=>|(?<name>[A-Za-z_]\w*)\s*=(?![=>])",
        RegexOptions.Compiled
    );

    private static string? DeclaredName(string statement)
    {
        var match = Declaration.Match(statement);
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>
    /// The statement around a position in blanked code: from the last <c>;</c>, <c>{</c> or
    /// <c>}</c> before it to the next one after it. Literals and comments are blanked, so a
    /// brace inside one does not cut the statement short.
    /// </summary>
    private static string Statement(string code, int position)
    {
        char[] ends = [';', '{', '}'];
        var start = position == 0 ? 0 : code.LastIndexOfAny(ends, position - 1) + 1;
        var end = code.IndexOfAny(ends, position);
        return code[start..(end < 0 ? code.Length : end)];
    }

    /// <summary>
    /// Where a name is used: as code, or as an interpolation hole (<c>{Adr}</c>), which the
    /// blanking pass hides inside its string.
    /// </summary>
    private static IEnumerable<int> References(
        string text,
        string code,
        string literals,
        string name
    ) =>
        Regex
            .Matches(text, $@"\b{Regex.Escape(name)}\b")
            .Select(m => m.Index)
            .Where(at => code[at] != ' ' || (literals[at] == ' ' && at > 0 && text[at - 1] == '{'));

    /// <summary>
    /// Null when the class declares at least one test that runs; otherwise why it does not.
    ///
    /// <para>
    /// Resolution checked only that the class exists and names the ADR. A
    /// <c>[TestFixture, Explicit]</c> class with no tests passed, and so did one whose every
    /// test was ignored, so an ADR could claim enforcement from a class CI never executes.
    /// </para>
    /// </summary>
    public static string? WhyItRunsNothing(string source, string className)
    {
        var code = CSharp.WithoutCommentsAndLiterals(source.Replace("\r\n", "\n"));
        var declaration = Regex.Match(code, $@"\bclass\s+{Regex.Escape(className)}\b");
        if (!declaration.Success)
            return null; // resolution reports a missing class on its own

        char[] ends = [';', '{', '}'];
        var headerStart = code.LastIndexOfAny(ends, declaration.Index) + 1;
        if (Skipped(code[headerStart..declaration.Index]))
            return "it is marked [Explicit] or [Ignore], so CI never runs it.";

        var open = code.IndexOf('{', declaration.Index);
        var close = open < 0 ? -1 : MatchingBrace(code, open);
        var body = open < 0 ? string.Empty : code[(open + 1)..(close < 0 ? code.Length : close)];

        var tests = TestAttribute.Matches(body).Select(t => TestHeader(body, t.Index)).ToList();
        if (tests.Count == 0)
            return "it declares no [Test], [TestCase], [TestCaseSource], [Theory] or [Fact] method.";

        return tests.All(Skipped)
            ? "every test it declares is marked [Explicit], [Ignore] or Skip, so CI never runs one."
            : null;
    }

    private static readonly Regex TestAttribute = new(
        @"(?<=[\[,]\s*)(?:Test|TestCase|TestCaseSource|Theory|Fact)(?:Attribute)?(?=\s*[(\],])",
        RegexOptions.Compiled
    );

    private static readonly Regex SkipAttribute = new(
        @"(?<=[\[,]\s*)(?:Explicit|Ignore)(?:Attribute)?(?=\s*[(\],])|\bSkip\s*=",
        RegexOptions.Compiled
    );

    private static bool Skipped(string header) => SkipAttribute.IsMatch(header);

    /// <summary>A test method's attributes and signature, up to where its body starts.</summary>
    private static string TestHeader(string body, int attribute)
    {
        char[] ends = [';', '{', '}'];
        var start = body.LastIndexOfAny(ends, attribute) + 1;
        var brace = body.IndexOf('{', attribute);
        var arrow = body.IndexOf("=>", attribute, StringComparison.Ordinal);
        var end = new[] { brace, arrow, body.Length }.Where(i => i >= 0).Min();
        return body[start..end];
    }

    private static int MatchingBrace(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{')
                depth++;
            else if (code[i] == '}' && --depth == 0)
                return i;
        }
        return -1;
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

                foreach (var (name, adr) in Declarations(raw))
                    declared.Add((name, adr, file));
            }
        }

        return (map, declared);
    }

    /// <summary>
    /// Every class in one source file that carries an ADR attribute, paired with each ADR it
    /// names. Public because the census must credit a class by its declaration, not by a name
    /// another class may share.
    /// </summary>
    public static List<(string Name, string Adr)> Declarations(string raw)
    {
        var declared = new List<(string, string)>();

        // Line-based, because the attribute's value is a string literal: it does not
        // survive the blanking pass the class scan uses. Classify is what keeps a
        // commented-out attribute from counting. An attribute left open at the end of a
        // line, as csharpier leaves a long one with an argument per line, is joined with
        // the lines that close it and read as one.
        var pending = new List<string>();
        string? open = null;
        var openLines = 0;
        foreach (var rawLine in raw.Replace("\r\n", "\n").Split('\n'))
        {
            if (CSharp.Classify(rawLine) != CSharp.LineKind.Code)
                continue;

            var line = rawLine;
            if (open is not null)
            {
                open += " " + line.Trim();
                openLines++;
                if (!Closed(open))
                {
                    // An attribute that never closes is malformed; stop joining rather
                    // than swallow the rest of the file.
                    if (openLines >= MaxAttributeLines)
                        open = null;
                    continue;
                }

                line = open;
                open = null;
            }
            else if (line.TrimStart().StartsWith('[') && !Closed(line))
            {
                open = line.Trim();
                openLines = 1;
                continue;
            }

            if (IsAdrAttribute(line))
            {
                // Every ADR the attributes above a class name belongs to that class. Keeping
                // only the last let a second attribute silently cancel the first.
                pending.AddRange(GuardProperty.Matches(line).Select(m => m.Groups["adr"].Value));
                continue;
            }

            var declaration = ClassDeclaration.Match(line);
            if (declaration.Success)
            {
                foreach (var adr in pending)
                    declared.Add((declaration.Groups["name"].Value, adr));
                pending.Clear();
                continue;
            }

            // Anything else between the attribute and a class ends the pairing, so a
            // class declaration quoted in a fixture string cannot inherit it.
            if (!line.TrimStart().StartsWith('[') && line.Trim().Length > 0)
                pending.Clear();
        }

        return declared;
    }
}
