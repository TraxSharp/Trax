namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every SDK reference page names the package, namespace and source files it documents, and
/// those exist: the package is a packable project, the namespace is in that package's public API
/// baseline, the source paths are in the checkout. The names its <c>## Signature</c> fences
/// declare are in the baseline too.
///
/// <para>A reference page describes code, and the only way to keep it describing code that
/// exists is to say which code and check it. The front matter is what the site renders as the
/// page's Package, Namespace and Source line, so a wrong value is a wrong link on the page, and a
/// signature naming a method the package does not have is the drift a reader trusts most.</para>
///
/// <para>Leaf pages are those under <c>sdk-reference/&lt;dir&gt;/</c>; <c>sdk-reference.md</c> and
/// <c>sdk-reference/&lt;dir&gt;.md</c> are indexes. A page with <c>surface: graphql</c> (the
/// wire schema) or <c>surface: typescript</c> (the npm package) has no C# namespace; a typescript
/// page's <c>package</c> names an npm package in the repository.</para>
///
/// <para>The pages that predate the rule are listed in <c>KnownIncompleteSdkPages.txt</c> (a
/// field missing) and <c>KnownSignatureMismatches.txt</c> (a declared name not in the
/// baseline). Both only shrink. A value that is present and wrong is never exempt.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class SdkReferenceFrontMatterTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private const string KnownIncompleteFile = "tests/Trax.Docs.Tests/KnownIncompleteSdkPages.txt";

    private const string KnownMismatchFile = "tests/Trax.Docs.Tests/KnownSignatureMismatches.txt";

    private static readonly string[] Surfaces = ["graphql", "typescript"];

    private static readonly Regex Namespace = new(
        @"^namespace (?<name>[A-Za-z_][\w.]*)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex Word = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    private static readonly Regex FenceOpen = new(
        @"^(?<indent>[ \t]*)(?<fence>`{3,}|~{3,})(?<info>.*)$",
        RegexOptions.Compiled
    );

    private static readonly Regex Heading = new(
        @"^ {0,3}#{1,2}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.Compiled
    );

    private static readonly Regex TypeDeclaration = new(
        @"\b(?:class|interface|record(?:[ \t]+(?:struct|class))?|struct|enum)[ \t]+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled
    );

    private static readonly Regex DelegateDeclaration = new(
        @"\bdelegate[ \t]+[^;(]*?(?<name>[A-Za-z_]\w*)[ \t]*(?:<[^()<>]*(?:<[^()<>]*>[^()<>]*)*>)?[ \t]*\(",
        RegexOptions.Compiled
    );

    // A name, its type parameters (one level of nesting), then its parameter list:
    // `Schedule<TTrain>(`. The type parameters hold no `<` or `>` of their own, or
    // `Task<Manifest> ScheduleAsync<T>(` would read as one name, `Task`.
    private static readonly Regex BeforeParameters = new(
        @"\b(?<name>[A-Za-z_]\w*)[ \t]*(?:<[^()<>]*(?:<[^()<>]*>[^()<>]*)*>)?[ \t]*\(",
        RegexOptions.Compiled
    );

    private static readonly Regex BeforeAccessors = new(
        @"(?<name>[A-Za-z_]\w*)[ \t]*\{[ \t]*(?:get|set|init)\b",
        RegexOptions.Compiled
    );

    private static readonly Regex LastWord = new(@"(?<word>[A-Za-z_]\w*)$", RegexOptions.Compiled);

    private static readonly Regex Literal = new(
        @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)'",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Words that are never a declared name, and the words after which a name followed by
    /// <c>(</c> is a call or an expression, not a declaration.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract",
        "as",
        "async",
        "await",
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
        "dynamic",
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
        "get",
        "goto",
        "if",
        "implicit",
        "in",
        "init",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "nameof",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "partial",
        "private",
        "protected",
        "public",
        "readonly",
        "record",
        "ref",
        "required",
        "return",
        "sbyte",
        "sealed",
        "set",
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
        "var",
        "virtual",
        "void",
        "volatile",
        "when",
        "where",
        "while",
        "with",
        "yield",
    };

    /// <summary>The modifiers and return types that may stand before a declared member's name.</summary>
    private static readonly HashSet<string> DeclarationPrefixes = new(StringComparer.Ordinal)
    {
        "abstract",
        "async",
        "bool",
        "byte",
        "char",
        "class",
        "decimal",
        "double",
        "dynamic",
        "extern",
        "float",
        "int",
        "interface",
        "internal",
        "long",
        "object",
        "override",
        "partial",
        "private",
        "protected",
        "public",
        "readonly",
        "record",
        "required",
        "sbyte",
        "sealed",
        "short",
        "static",
        "string",
        "struct",
        "uint",
        "ulong",
        "unsafe",
        "ushort",
        "virtual",
        "void",
        "volatile",
    };

    [Test]
    public void Every_SdkReferencePage_NamesItsPackageNamespaceAndSource()
    {
        var npm = Projects.NpmPackages();
        var errors = new List<string>();
        var incomplete = new HashSet<string>(StringComparer.Ordinal);
        var missingFields = new List<string>();

        foreach (var page in LeafPages())
        {
            var (missing, wrong) = Check(FrontMatter.OfPage(page), npm);
            errors.AddRange(wrong.Select(w => $"{page}  -> {w}"));
            if (missing.Count > 0)
            {
                incomplete.Add(page);
                missingFields.Add($"{page}  (no {string.Join(", no ", missing)})");
            }
        }

        var known = Ratchet.Entries(KnownIncompleteFile);
        var offenders = missingFields.Where(m => !known.Contains(m.Split("  ")[0])).ToList();
        var stale = Ratchet.Stale(known, incomplete);

        errors
            .Should()
            .BeEmpty(
                "an SDK reference page's `package:` names a packable project (or, for "
                    + "`surface: typescript`, an npm package in the repository), each `namespace:` "
                    + "is declared in that package's public API baseline when it has one, every "
                    + "`source:` path exists relative to the repository root, and `surface:` is "
                    + $"graphql or typescript when set. See {Adr} and "
                    + "/docs/contributing/writing-docs. Wrong:\n  "
                    + string.Join("\n  ", errors)
            );

        offenders
            .Should()
            .BeEmpty(
                "an SDK reference page states `package:`, `namespace:` (unless it sets "
                    + "`surface: graphql` or `surface: typescript`) and `source:` in its front "
                    + $"matter. See {Adr}. Do not add the page to {KnownIncompleteFile}. Missing:\n  "
                    + string.Join("\n  ", offenders)
            );

        stale
            .Should()
            .BeEmpty(
                $"{KnownIncompleteFile} only shrinks: delete each line whose page now states all "
                    + $"three, or no longer exists. See {Adr}. Stale:\n  "
                    + string.Join("\n  ", stale)
            );
    }

    [Test]
    public void Every_SignatureName_IsInThePackagesBaseline()
    {
        var mismatched = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var checkedPages = 0;

        foreach (var page in LeafPages())
        {
            var package = FrontMatter.OfPage(page).Scalar("package");
            if (
                package is null
                || !Projects.Packable.TryGetValue(package, out var project)
                || project.Baseline is null
            )
                continue;

            checkedPages++;
            var words = Word.Matches(File.ReadAllText(project.Baseline))
                .Select(m => m.Value)
                .ToHashSet(StringComparer.Ordinal);

            var missing = SignatureFences(File.ReadAllText(RepoRoot.Combine(page.Split('/'))))
                .SelectMany(f => DeclaredNames(f.Code).Select(n => (f.Line, Name: n)))
                .Where(x => !words.Contains(x.Name))
                .Select(x => $"{x.Name} (line {x.Line})")
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (missing.Count > 0)
                mismatched[page] = missing;
        }

        var known = Ratchet.Entries(KnownMismatchFile);
        var offenders = mismatched
            .Where(m => !known.Contains(m.Key))
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .Select(m => $"{m.Key}  -> {string.Join(", ", m.Value)}")
            .ToList();
        var stale = Ratchet.Stale(known, mismatched.Keys.ToHashSet(StringComparer.Ordinal));

        offenders
            .Should()
            .BeEmpty(
                "a `## Signature` fence on an SDK reference page declares what the page's "
                    + "`package:` ships, so every type it declares (after class, interface, "
                    + "record, struct, enum, delegate) and every member (the name before its "
                    + "parameter list or its `{ get`) is a name in that package's public API "
                    + "baseline. Fix the signature to match the code, or the `package:` to the one "
                    + $"that ships it. Checked {checkedPages} pages. See {Adr}. Do not add the page "
                    + $"to {KnownMismatchFile}. Not in the baseline:\n  "
                    + string.Join("\n  ", offenders)
            );

        stale
            .Should()
            .BeEmpty(
                $"{KnownMismatchFile} only shrinks, and applies only to a page whose package has "
                    + "a baseline: delete each line whose page now matches, has no such package, "
                    + $"or no longer exists. See {Adr}. Stale:\n  "
                    + string.Join("\n  ", stale)
            );
    }

    [TestCase(KnownIncompleteFile)]
    [TestCase(KnownMismatchFile)]
    public void KnownPages_AreSortedAndUnique(string file) =>
        Ratchet
            .IsSortedAndUnique(Ratchet.Entries(file))
            .Should()
            .BeTrue($"{file} lists each page once, in ordinal order. See {Adr}.");

    [Test]
    public void DeclaredNames_AreTypesAndMembers_NotCallsOrParameters()
    {
        const string code = """
            namespace Trax.Scheduler.Configuration;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ScheduleOptions : IEquatable<ScheduleOptions>
            {
                public string Group { get; init; } = "default (x)";
                public ScheduleOptions Priority(int priority, TimeSpan? delay = null) { }
                public static implicit operator string(ScheduleOptions o) { }
            }

            public SchedulerConfigurationBuilder Schedule<TTrain>(
                string externalId,
                IManifestProperties input,
                Action<ScheduleOptions>? options = null
            )
                where TTrain : class, new()

            Task<Manifest> ScheduleAsync<TTrain, TInput>(TInput input, CancellationToken ct = default);
            (int Count, string Name) Pair();
            public delegate Task JunctionHandler<TIn>(TIn input);
            public record struct Window(DateTime Start, DateTime End);
            public enum Shade { NotReached = 0 }

            services.AddTrax(trax => trax.AddEffects(e => e.UseInMemory()));
            var x = Build(services);
            return new Widget(1);
            """;

        DeclaredNames(code)
            .Should()
            .BeEquivalentTo([
                "ScheduleOptions",
                "Group",
                "Priority",
                "Schedule",
                "ScheduleAsync",
                "Pair",
                "JunctionHandler",
                "Window",
                "Shade",
            ]);
    }

    [Test]
    public void SignatureFences_AreTheCSharpFencesUnderSignatureHeadings()
    {
        const string page = """
            ---
            title: X
            ---

            # X

            ```csharp fragment
            public void Before();
            ```

            ## Signatures

            ### Startup

            ```csharp fragment
            public void One();
            ```

            ```graphql
            type Query { two: Int }
            ```

            ## Example

            ```csharp compile
            Three();
            ```
            """;

        SignatureFences(page).Select(f => f.Code.Trim()).Should().Equal("public void One();");
    }

    [Test]
    public void Check_ReportsMissingFieldsApart_FromWrongOnes()
    {
        var npm = new HashSet<string>(StringComparer.Ordinal) { "@traxsharp/state-machine" };

        Check(FrontMatter.Of("---\ntitle: X\n---\n"), npm)
            .Missing.Should()
            .Equal("package", "namespace", "source");

        var graphql = Check(
            FrontMatter.Of(
                "---\npackage: Trax.Api.GraphQL\nsurface: graphql\nsource: [Trax.Api]\n---\n"
            ),
            npm
        );
        graphql.Missing.Should().BeEmpty();
        graphql.Wrong.Should().BeEmpty();

        var typescript = Check(
            FrontMatter.Of(
                "---\npackage: \"@traxsharp/state-machine\"\nsurface: typescript\nsource:\n  - Trax.Api.StateMachine\n---\n"
            ),
            npm
        );
        typescript.Missing.Should().BeEmpty();
        typescript.Wrong.Should().BeEmpty();

        var wrong = Check(
            FrontMatter.Of(
                "---\npackage: Trax.Nope\nnamespace: Trax.Nope\nsurface: rest\nsource:\n  - Trax.Nope/x.cs\n  - ../etc\n---\n"
            ),
            npm
        );
        wrong.Missing.Should().BeEmpty();
        wrong
            .Wrong.Should()
            .HaveCount(4)
            .And.Contain(w => w.Contains("Trax.Nope is not a packable project"))
            .And.Contain(w => w.Contains("surface 'rest'"))
            .And.Contain(w => w.Contains("Trax.Nope/x.cs does not exist"))
            .And.Contain(w => w.Contains("../etc"));
    }

    [Test]
    public void Check_ReadsTheNamespaceFromTheBaseline()
    {
        var npm = new HashSet<string>(StringComparer.Ordinal);
        const string source = "source: [Trax.Scheduler/src/Trax.Scheduler]";

        Check(
            FrontMatter.Of(
                $"---\npackage: Trax.Scheduler\nnamespace: Trax.Scheduler.Configuration\n{source}\n---\n"
            ),
            npm
        )
            .Wrong.Should()
            .BeEmpty();
        Check(
            FrontMatter.Of(
                $"---\npackage: Trax.Scheduler\nnamespace: [Trax.Scheduler.Configuration, Trax.Scheduler.Nope]\n{source}\n---\n"
            ),
            npm
        )
            .Wrong.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Trax.Scheduler.Nope");
    }

    [Test]
    public void Packable_IsTheShippedProjects()
    {
        Projects
            .Packable.Should()
            .ContainKeys("Trax.Core", "Trax.Scheduler", "Trax.Cli", "Trax.Runner.Lambda");
        Projects
            .Packable.Keys.Should()
            .NotContain(k => k.EndsWith(".Tests", StringComparison.Ordinal));
        Projects.Packable["Trax.Cli"].Baseline.Should().EndWith("trax.received.txt");
        Projects
            .Packable["Trax.Scheduler"]
            .Baseline.Should()
            .EndWith("Trax.Scheduler.received.txt");
    }

    /// <summary>The published SDK reference pages below the section and folder indexes.</summary>
    private static IEnumerable<string> LeafPages() =>
        RepoRoot.PublishedPages().Where(DocsSite.IsSdkReferenceLeaf);

    /// <summary>
    /// The fields the page leaves out (<c>package</c>, <c>namespace</c>, <c>source</c>), and the
    /// ones it states wrongly.
    /// </summary>
    private static (List<string> Missing, List<string> Wrong) Check(
        FrontMatter frontMatter,
        IReadOnlySet<string> npm
    )
    {
        var missing = new List<string>();
        var wrong = new List<string>();

        var surface = frontMatter.Scalar("surface");
        if (surface is not null && !Surfaces.Contains(surface, StringComparer.Ordinal))
            wrong.Add($"surface '{surface}' is not one of {string.Join(", ", Surfaces)}");

        var package = frontMatter.Scalar("package");
        Projects.Package? project = null;
        if (package is null)
            missing.Add("package");
        else if (Projects.Packable.TryGetValue(package, out var found))
            project = found;
        else if (!(surface == "typescript" && npm.Contains(package)))
            wrong.Add(
                surface == "typescript"
                    ? $"package {package} is neither a packable project nor an npm package in the repository"
                    : $"package {package} is not a packable project (a csproj under a Trax.*/src/ folder)"
            );

        var namespaces = frontMatter.List("namespace");
        if (namespaces.Count == 0 && surface is not ("graphql" or "typescript"))
            missing.Add("namespace");
        else if (project?.Baseline is { } baseline && namespaces.Count > 0)
        {
            var declared = Namespace
                .Matches(File.ReadAllText(baseline))
                .Select(m => m.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);
            wrong.AddRange(
                namespaces
                    .Where(n => !declared.Contains(n))
                    .Select(n => $"namespace {n} is not in the public API baseline of {package}")
            );
        }

        var sources = frontMatter.List("source");
        if (sources.Count == 0)
            missing.Add("source");
        foreach (var source in sources)
        {
            if (source.StartsWith('/') || source.Contains('\\') || source.Split('/').Contains(".."))
                wrong.Add(
                    $"source {source} is not a forward-slash path relative to the repository root"
                );
            else if (!RepoRoot.ExistsInMonorepo(source))
                wrong.Add($"source {source} does not exist");
        }

        return (missing, wrong);
    }

    /// <summary>
    /// The C# fences under a <c>## Signature</c> or <c>## Signatures</c> heading, with the line
    /// of each fence's opening. A level 3 heading stays inside the section.
    /// </summary>
    private static List<(int Line, string Code)> SignatureFences(string markdown)
    {
        var fences = new List<(int, string)>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inSignature = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var open = FenceOpen.Match(lines[i]);
            if (!open.Success)
            {
                if (Heading.Match(lines[i]) is { Success: true } h)
                    inSignature = h.Groups["text"].Value is "Signature" or "Signatures";
                continue;
            }

            var fence = open.Groups["fence"].Value;
            var info = open.Groups["info"].Value.Trim();
            if (fence[0] == '`' && info.Contains('`'))
                continue;

            var closing = new Regex(
                $@"^[ \t]*{Regex.Escape(fence[0].ToString())}{{{fence.Length},}}[ \t]*$"
            );
            var body = new List<string>();
            var j = i + 1;
            while (j < lines.Length && !closing.IsMatch(lines[j]))
                body.Add(lines[j++]);

            var language = info.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?.ToLowerInvariant();
            if (inSignature && language is "csharp" or "cs" or "c#")
                fences.Add((i + 1, string.Join("\n", body)));
            i = j;
        }

        return fences;
    }

    /// <summary>
    /// The names a signature declares, read conservatively: type names after
    /// <c>class</c>/<c>interface</c>/<c>record</c>/<c>struct</c>/<c>enum</c>/<c>delegate</c>, a
    /// member's name where it stands directly after its type or a modifier and before its
    /// parameter list, and a property's name before <c>{ get</c>. Calls, parameter names, type
    /// parameters, attributes, comments and literals are not declarations and are skipped.
    /// </summary>
    private static HashSet<string> DeclaredNames(string code)
    {
        var lines = code.Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => Literal.Replace(l, "\"\""))
            .Select(l =>
                l.Contains("//", StringComparison.Ordinal)
                    ? l[..l.IndexOf("//", StringComparison.Ordinal)]
                    : l
            )
            .Where(l => !l.TrimStart().StartsWith('['));
        var text = string.Join("\n", lines);

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in TypeDeclaration.Matches(text))
            names.Add(m.Groups["name"].Value);
        foreach (Match m in DelegateDeclaration.Matches(text))
            names.Add(m.Groups["name"].Value);
        foreach (Match m in BeforeAccessors.Matches(text))
            names.Add(m.Groups["name"].Value);

        foreach (Match m in BeforeParameters.Matches(text))
        {
            var before = text[..m.Index].TrimEnd(' ', '\t');
            if (before.Length == 0 || before[^1] == '\n')
                continue;

            // A declaration's name follows its return type (`Task<Manifest> X(`, `Widget X(`,
            // `int[] X(`, `string? X(`, `(int, string) X(`) or a modifier. After `.`, `=`, `(`
            // or `,` it is a call, and after `new`, `return` or `await` an expression. A record's
            // name stands before its parameter list too (`record Window(`).
            var last = before[^1];
            if (!(char.IsLetterOrDigit(last) || last is '_' or '>' or ']' or '?' or ')'))
                continue;
            if (LastWord.Match(before) is { Success: true } word)
            {
                var previous = word.Groups["word"].Value;
                if (Keywords.Contains(previous) && !DeclarationPrefixes.Contains(previous))
                    continue;
            }

            names.Add(m.Groups["name"].Value);
        }

        names.RemoveWhere(Keywords.Contains);
        return names;
    }
}
