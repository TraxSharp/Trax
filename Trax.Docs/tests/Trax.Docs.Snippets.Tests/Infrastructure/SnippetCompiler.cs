using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// The fences of one page that compile together: one fence marked <c>compile</c>, or every fence
/// on the page that names the same <c>compile=group</c>.
///
/// <para>
/// A page whose front matter says <c>follows: book/an-order-is-a-train</c> carries on a project the
/// page it names started, as a chapter of the book carries on the last one. Each of its groups
/// compiles with the followed page's group of the same name, and that page's own followed page,
/// back to the first. A fence that names a file (<c>file=Program.cs</c>) replaces the fence before
/// it that names the same file, so a chapter shows only the files it adds or changes and still
/// compiles as the whole project.
/// </para>
/// <para>
/// A group named <c>tests</c> is the project's test project. It compiles as an assembly of its own,
/// with the implicit usings of a plain <c>Microsoft.NET.Sdk</c> project and the test frameworks a
/// test project has (<see cref="SnippetCompiler.TestFrameworkAssemblyPrefixes"/>), against the
/// page's <c>app</c> group compiled as the project it references, whose internals it sees
/// (<see cref="App"/>).
/// </para>
/// </summary>
public sealed record SnippetUnit(string Page, string Group, IReadOnlyList<Fence> Fences)
{
    /// <summary>
    /// For a <see cref="TestsGroup"/> unit, the fences of the project it tests, which it references;
    /// empty otherwise.
    /// </summary>
    public IReadOnlyList<Fence> App { get; init; } = [];

    /// <summary>The group a page's test project is in.</summary>
    public const string TestsGroup = "tests";

    /// <summary>The group a <see cref="TestsGroup"/> compiles against.</summary>
    public const string AppGroup = "app";

    public override string ToString() => $"{Page} [{Group}]";

    /// <summary>Whether this unit is a test project, compiled with the test frameworks.</summary>
    public bool IsTests => Group == TestsGroup;

    /// <summary>The compiled fences of every page, one unit per page and group.</summary>
    public static IEnumerable<SnippetUnit> InDocs() =>
        WithTheAppInTheTests(
            Following(
                    Own(Infrastructure.Fences.InDocs()).ToList(),
                    SnippetCompiler.FollowedPageOf,
                    SnippetCompiler.MovesOf
                )
                .ToList(),
            SnippetCompiler.FollowedPageOf
        );

    /// <summary>
    /// Each <see cref="TestsGroup"/> unit with the <see cref="AppGroup"/> fences of its page, or of
    /// the nearest page before it in its chain that has them, as the project it tests; every other
    /// unit as it is.
    /// </summary>
    public static IEnumerable<SnippetUnit> WithTheAppInTheTests(
        IReadOnlyList<SnippetUnit> units,
        Func<string, string?>? followedPageOf = null
    )
    {
        var apps = units.Where(u => u.Group == AppGroup).ToDictionary(u => u.Page);
        return units.Select(u =>
        {
            // A test project, or on a page in a chain, any project but the app: each is an assembly
            // of its own that references the app, as a book's worker references its library.
            var chained =
                followedPageOf is not null
                && (
                    followedPageOf(u.Page) is not null
                    || units.Any(o => followedPageOf(o.Page) == u.Page)
                );
            if (u.Group == AppGroup || !(u.IsTests || chained))
                return u;
            var page = (string?)u.Page;
            while (page is not null && !apps.ContainsKey(page))
                page = followedPageOf?.Invoke(page);
            return page is null ? u : u with { App = apps[page].Fences };
        });
    }

    /// <summary>The units the fences make on their own pages, before any page follows another.</summary>
    public static IEnumerable<SnippetUnit> Own(IEnumerable<Fence> fences) =>
        fences
            .Where(f => f.IsCompiled && f.IsCSharp)
            .SelectMany(f => f.Groups.Select(g => (Fence: f, Group: g)))
            .GroupBy(x => (x.Fence.Page, x.Group))
            .OrderBy(g => g.Key.Page, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Group, StringComparer.Ordinal)
            .Select(g => new SnippetUnit(
                g.Key.Page,
                g.Key.Group,
                g.Select(x => x.Fence).OrderBy(f => f.OpenLine).ToList()
            ));

    /// <summary>
    /// Each unit with the fences of the pages it follows, oldest first, a fence naming a file
    /// replacing an earlier one that names the same file.
    /// </summary>
    /// <param name="units">Every page's own units.</param>
    /// <param name="followedPageOf">The page a page follows, or null.</param>
    public static IEnumerable<SnippetUnit> Following(
        IReadOnlyList<SnippetUnit> units,
        Func<string, string?> followedPageOf,
        Func<string, IReadOnlyList<(string From, string To)>>? movesOf = null
    )
    {
        var byPage = units.ToDictionary(u => (u.Page, u.Group));

        foreach (var unit in units)
        {
            var chain = ChainOf(unit.Page, followedPageOf);
            if (chain.Count == 0)
            {
                yield return unit;
                continue;
            }

            var fences = new List<Fence>();
            foreach (var page in chain.Append(unit.Page))
            {
                // A page that moves files moves the ones the pages before it showed, before its own
                // fences replace any of them.
                foreach (var (from, to) in movesOf?.Invoke(page) ?? [])
                    for (var i = 0; i < fences.Count; i++)
                        if (
                            fences[i].File is { } file
                            && file.StartsWith(from, StringComparison.Ordinal)
                        )
                            fences[i] = fences[i] with { File = to + file[from.Length..] };
                if (!byPage.TryGetValue((page, unit.Group), out var earlier))
                    continue;
                foreach (var fence in earlier.Fences)
                {
                    if (fence.File is not null)
                        fences.RemoveAll(f => f.File == fence.File);
                    fences.Add(fence);
                }
            }

            yield return unit with
            {
                Fences = fences,
            };
        }
    }

    /// <summary>
    /// The pages <paramref name="page"/> follows, the first page of the chain first. Throws on a
    /// chain that comes back to a page it has passed.
    /// </summary>
    public static IReadOnlyList<string> ChainOf(string page, Func<string, string?> followedPageOf)
    {
        var chain = new List<string>();
        for (var at = followedPageOf(page); at is not null; at = followedPageOf(at))
        {
            if (at == page || chain.Contains(at))
                throw new InvalidOperationException(
                    $"{page} follows a chain of pages that comes back to {at}."
                );
            chain.Add(at);
        }
        chain.Reverse();
        return chain;
    }
}

/// <summary>
/// Compiles a page's snippets the way a reader's project would compile them.
///
/// <para>
/// Each fence becomes its own source file, as each would be its own file in a project, and a
/// <c>#line</c> directive maps it back to the page so a diagnostic names the page and the line on
/// it. The compilation sees the implicit usings of a <c>Microsoft.NET.Sdk.Web</c> project with
/// <c>ImplicitUsings</c> on, nullable reference types on, and the assemblies this test project
/// references: the Trax projects of the same checkout and the framework. Trax's own namespaces are
/// not implied, so a snippet shows its <c>using</c> lines.
/// </para>
/// <para>
/// A file holding top-level statements makes the unit an executable, as <c>Program.cs</c> does;
/// otherwise it is a library. Warnings fail the snippet as errors do, because a reader who copies
/// it into a project built with warnings as errors gets the same failure. CS1701 and CS1702 are
/// the exception: MSBuild suppresses them in every C# project, so they are suppressed here too.
/// </para>
/// <para>
/// Context files in this project join a page's compilations, and the reader never sees them.
/// They layer: <c>Context/_shared.cs</c> declares the host variables any snippet may assume
/// (<c>builder</c>, <c>services</c>, <c>app</c>, <c>connectionString</c>, ...) and joins every
/// page; <c>Context/&lt;dir&gt;/_section.cs</c> joins every page under that directory, at any
/// depth; <c>Context/&lt;page path&gt;.cs</c> declares the types one page only implies (an
/// <c>IOrderTrain</c> the example assumes exists). A page whose front matter says
/// <c>snippet_context: none</c> gets its own file only, so it compiles as a reader's empty
/// project would.
/// </para>
/// </summary>
public static class SnippetCompiler
{
    /// <summary>The usings a <c>Microsoft.NET.Sdk.Web</c> project gets from <c>ImplicitUsings</c>.</summary>
    public static readonly IReadOnlyList<string> ImplicitUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Net.Http.Json",
        "System.Threading",
        "System.Threading.Tasks",
        "Microsoft.AspNetCore.Builder",
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Logging",
    ];

    /// <summary>
    /// Assemblies on this project's reference list that a reader's project would not have: the
    /// test framework and the compiler doing the checking.
    /// </summary>
    private static readonly string[] TestOnlyAssemblyPrefixes =
    [
        "nunit.",
        "NUnit3.",
        "AwesomeAssertions",
        "Microsoft.AspNetCore.Mvc.Testing",
        "Microsoft.AspNetCore.TestHost",
        "Microsoft.CodeAnalysis",
        "Microsoft.TestPlatform",
        "Microsoft.VisualStudio.TestPlatform",
        "Microsoft.VisualStudio.CodeCoverage",
        "testhost",
    ];

    /// <summary>
    /// The test frameworks a reader's test project references, which a <c>tests</c> group compiles
    /// with and every other group does not.
    /// </summary>
    public static readonly IReadOnlyList<string> TestFrameworkAssemblyPrefixes =
    [
        "nunit.",
        "AwesomeAssertions",
        "Microsoft.AspNetCore.Mvc.Testing",
        "Microsoft.AspNetCore.TestHost",
    ];

    /// <summary>
    /// The warnings every C# project built by MSBuild suppresses by default (<c>NoWarn</c> is
    /// <c>1701;1702</c> in <c>Microsoft.CSharp.Core.targets</c>): an assembly referencing an older
    /// version of an assembly than the one referenced here. A reader's project unifies the versions
    /// and never sees them, so a snippet should not either.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ReportDiagnostic> MSBuildNoWarn =
        new Dictionary<string, ReportDiagnostic>
        {
            ["CS1701"] = ReportDiagnostic.Suppress,
            ["CS1702"] = ReportDiagnostic.Suppress,
        };

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
        LoadReferences(tests: false)
    );

    private static readonly Lazy<IReadOnlyList<MetadataReference>> TestReferences = new(() =>
        LoadReferences(tests: true)
    );

    /// <summary>The context file every page's compilations get, unless the page opts out.</summary>
    public const string SharedFile = "_shared.cs";

    /// <summary>The context file every page under its directory gets, at any depth.</summary>
    public const string SectionFile = "_section.cs";

    private static readonly Regex NoSnippetContext = new(
        @"\A---\n(?:(?!---\n).*\n)*?snippet_context:[ \t]*none[ \t]*\n(?:.*\n)*?---(?:\n|\z)",
        RegexOptions.Compiled
    );

    /// <summary>The folder holding the context files, mirroring the docs tree.</summary>
    public static string ContextRoot =>
        RepoRoot.Combine("tests", "Trax.Docs.Snippets.Tests", "Context");

    public static string ContextFileFor(string page) =>
        Path.Combine(ContextRoot, Path.ChangeExtension(page, ".cs"));

    /// <summary>
    /// The context files that exist for <paramref name="page"/>, widest first: the shared file,
    /// each enclosing directory's section file from the top down, then the page's own file.
    /// </summary>
    public static IReadOnlyList<string> ContextFilesFor(string page)
    {
        var files = new List<string>();

        if (!OptsOutOfSharedContext(page))
        {
            files.Add(Path.Combine(ContextRoot, SharedFile));

            var directories = page.Split('/')[..^1];
            for (var depth = 1; depth <= directories.Length; depth++)
                files.Add(
                    Path.Combine(
                        new[] { ContextRoot }
                            .Concat(directories.Take(depth))
                            .Append(SectionFile)
                            .ToArray()
                    )
                );
        }

        files.Add(ContextFileFor(page));
        return files.Where(File.Exists).ToList();
    }

    /// <summary>Whether the page's front matter says <c>snippet_context: none</c>.</summary>
    public static bool OptsOutOfSharedContext(string page)
    {
        var file = RepoRoot.Combine(page.Split('/'));
        return File.Exists(file)
            && OptsOutOfSharedContextIn(File.ReadAllText(file).Replace("\r\n", "\n"));
    }

    public static bool OptsOutOfSharedContextIn(string markdown) =>
        NoSnippetContext.IsMatch(markdown);

    private static readonly Regex FrontMatter = new(
        @"\A---\n(?<body>(?:(?!---\n).*\n)*?)---(?:\n|\z)",
        RegexOptions.Compiled
    );

    private static readonly Regex Move = new(
        @"^[ \t]+-[ \t]+""?(?<from>[^""\s]+)[ \t]+->[ \t]+(?<to>[^""\s]+?)""?[ \t]*$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The moves the page's front matter lists under <c>moves:</c>, each a path prefix of the files
    /// the pages before it showed and the prefix they move to (<c>- Trains/ -> Shopfront.Core/Trains/</c>).
    /// </summary>
    public static IReadOnlyList<(string From, string To)> MovesOf(string page)
    {
        var file = RepoRoot.Combine(page.Split('/'));
        return File.Exists(file) ? MovesIn(File.ReadAllText(file).Replace("\r\n", "\n")) : [];
    }

    /// <summary>The moves listed under <c>moves:</c> in the front matter of <paramref name="markdown"/>.</summary>
    public static IReadOnlyList<(string From, string To)> MovesIn(string markdown)
    {
        var body = FrontMatter.Match(markdown);
        if (!body.Success)
            return [];
        var moves = new List<(string, string)>();
        var inMoves = false;
        foreach (var line in body.Groups["body"].Value.Split('\n'))
        {
            if (!line.StartsWith(' ') && !line.StartsWith('\t'))
            {
                inMoves = line.TrimEnd() == "moves:";
                continue;
            }
            if (inMoves && Move.Match(line) is { Success: true } move)
                moves.Add((move.Groups["from"].Value, move.Groups["to"].Value));
        }
        return moves;
    }

    private static readonly Regex Follows = new(
        @"\A---\n(?:(?!---\n).*\n)*?follows:[ \t]*(?<page>\S+)[ \t]*\n(?:.*\n)*?---(?:\n|\z)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The page <paramref name="page"/> follows, from <c>follows:</c> in its front matter, as a
    /// path relative to the docs root (<c>book/an-order-is-a-train.md</c>). Null when it follows
    /// none or does not exist.
    /// </summary>
    public static string? FollowedPageOf(string page)
    {
        var file = RepoRoot.Combine(page.Split('/'));
        return File.Exists(file)
            ? FollowedPageIn(File.ReadAllText(file).Replace("\r\n", "\n"))
            : null;
    }

    /// <summary>The page the front matter's <c>follows:</c> names, with <c>.md</c>, or null.</summary>
    public static string? FollowedPageIn(string markdown)
    {
        var match = Follows.Match(markdown);
        return match.Success ? match.Groups["page"].Value.Trim('"', '\'', '/') + ".md" : null;
    }

    /// <summary>
    /// The unit's errors and warnings, each as <c>page.md:line:column: severity id: message</c>.
    /// Empty when it compiles cleanly.
    /// </summary>
    public static IReadOnlyList<string> Diagnose(SnippetUnit unit)
    {
        if (unit.App.Count > 0)
        {
            // The project it references: its own diagnostics belong to the app unit, which reports
            // them. It grants the tests its internals, as InternalsVisibleTo does.
            var app = Compile(
                AppAssemblyName,
                unit.Page,
                unit.App,
                ImplicitUsings,
                References.Value,
                $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{TestsAssemblyName}\")]"
            );
            return Report(
                Compile(
                    unit.IsTests ? TestsAssemblyName : $"{AppAssemblyName}.{unit.Group}",
                    unit.Page,
                    unit.Fences,
                    unit.IsTests ? TestImplicitUsings : ImplicitUsings,
                    [
                        .. unit.IsTests ? TestReferences.Value : References.Value,
                        app.ToMetadataReference(),
                    ],
                    withContext: false
                )
            );
        }

        return Report(
            Compile(
                AppAssemblyName,
                unit.Page,
                unit.Fences,
                ImplicitUsings,
                unit.IsTests ? TestReferences.Value : References.Value
            )
        );
    }

    private const string AppAssemblyName = "Snippet";

    private const string TestsAssemblyName = "Snippet.Tests";

    /// <summary>The usings a plain <c>Microsoft.NET.Sdk</c> project, as a test project is, gets.</summary>
    public static readonly IReadOnlyList<string> TestImplicitUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    private static CSharpCompilation Compile(
        string assemblyName,
        string page,
        IEnumerable<Fence> fences,
        IReadOnlyList<string> implicitUsings,
        IEnumerable<MetadataReference> references,
        string? extraSource = null,
        bool withContext = true
    )
    {
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                string.Concat(implicitUsings.Select(u => $"global using global::{u};\n"))
                    + (extraSource is null ? "" : extraSource + "\n"),
                ParseOptions,
                path: "ImplicitUsings.g.cs"
            ),
        };

        foreach (var fence in fences)
            trees.Add(
                CSharpSyntaxTree.ParseText(
                    $"#line {fence.FirstCodeLine} \"{fence.Page}\"\n{fence.Code}\n",
                    ParseOptions,
                    path: fence.Page
                )
            );

        // The context files join the project; a test project sees them through its reference.
        foreach (var context in withContext ? ContextFilesFor(page) : [])
            trees.Add(
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(context),
                    ParseOptions,
                    path: RepoRoot.Relative(context).Replace('\\', '/')
                )
            );

        var executable = trees.Any(t =>
            t.GetCompilationUnitRoot().Members.OfType<GlobalStatementSyntax>().Any()
        );

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(
                executable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: MSBuildNoWarn
            )
        );
    }

    private static IReadOnlyList<string> Report(CSharpCompilation compilation) =>
        compilation
            .GetDiagnostics()
            .Where(d =>
                !d.IsSuppressed
                && d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning
            )
            .OrderBy(d => d.Location.GetMappedLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location.GetMappedLineSpan().StartLinePosition.Line)
            .Select(Format)
            .ToList();

    private static bool StartsWith(string path, string prefix) =>
        Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetMappedLineSpan();
        var where = span.IsValid
            ? $"{span.Path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}"
            : "(no location)";
        var severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        return $"{where}: {severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    private static IReadOnlyList<MetadataReference> LoadReferences(bool tests)
    {
        var list = Path.Combine(AppContext.BaseDirectory, "snippet-references.txt");
        if (!File.Exists(list))
            throw new InvalidOperationException(
                $"'{list}' is missing. The WriteSnippetReferences target in "
                    + "Trax.Docs.Snippets.Tests.csproj writes it on build; build the project first."
            );

        return File.ReadAllLines(list)
            .Where(line => line.Length > 0)
            .Where(path =>
                !TestOnlyAssemblyPrefixes.Any(prefix => StartsWith(path, prefix))
                || (tests && TestFrameworkAssemblyPrefixes.Any(prefix => StartsWith(path, prefix)))
            )
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
