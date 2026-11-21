using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Docs.Snippets.Tests.Infrastructure;

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
public static partial class SnippetCompiler
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
        new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal)
        {
            ["CS1701"] = ReportDiagnostic.Suppress,
            ["CS1702"] = ReportDiagnostic.Suppress,
        };

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<Task<IReadOnlyList<MetadataReference>>> References = new(() =>
        LoadReferencesAsync(tests: false)
    );

    private static readonly Lazy<Task<IReadOnlyList<MetadataReference>>> TestReferences = new(() =>
        LoadReferencesAsync(tests: true)
    );

    /// <summary>The context file every page's compilations get, unless the page opts out.</summary>
    public const string SharedFile = "_shared.cs";

    /// <summary>The context file every page under its directory gets, at any depth.</summary>
    public const string SectionFile = "_section.cs";

    [GeneratedRegex(
        @"\A---\n(?:(?!---\n).*\n)*?snippet_context:[ \t]*none[ \t]*\n(?:.*\n)*?---(?:\n|\z)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex NoSnippetContext { get; }

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
            && OptsOutOfSharedContextIn(
                File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal)
            );
    }

    public static bool OptsOutOfSharedContextIn(string markdown) =>
        NoSnippetContext.IsMatch(markdown);

    [GeneratedRegex(
        @"\A---\n(?<body>(?:(?!---\n).*\n)*?)---(?:\n|\z)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex FrontMatter { get; }

    [GeneratedRegex(
        @"^[ \t]+-[ \t]+""?(?<from>[^""\s]+)[ \t]+->[ \t]+(?<to>[^""\s]+?)""?[ \t]*$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Move { get; }

    /// <summary>
    /// The moves the page's front matter lists under <c>moves:</c>, each a path prefix of the files
    /// the pages before it showed and the prefix they move to (<c>- Trains/ -> Shopfront.Core/Trains/</c>).
    /// </summary>
    public static IReadOnlyList<(string From, string To)> MovesOf(string page)
    {
        var file = RepoRoot.Combine(page.Split('/'));
        return File.Exists(file)
            ? MovesIn(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal))
            : [];
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
                inMoves = string.Equals(line.TrimEnd(), "moves:", StringComparison.Ordinal);
                continue;
            }
            if (inMoves && Move.Match(line) is { Success: true } move)
                moves.Add((move.Groups["from"].Value, move.Groups["to"].Value));
        }
        return moves;
    }

    [GeneratedRegex(
        @"\A---\n(?:(?!---\n).*\n)*?follows:[ \t]*(?<page>\S+)[ \t]*\n(?:.*\n)*?---(?:\n|\z)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Follows { get; }

    /// <summary>
    /// The page <paramref name="page"/> follows, from <c>follows:</c> in its front matter, as a
    /// path relative to the docs root (<c>book/an-order-is-a-train.md</c>). Null when it follows
    /// none or does not exist.
    /// </summary>
    public static string? FollowedPageOf(string page)
    {
        var file = RepoRoot.Combine(page.Split('/'));
        return File.Exists(file)
            ? FollowedPageIn(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal))
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
    public static async Task<IReadOnlyList<string>> DiagnoseAsync(SnippetUnit unit)
    {
        var references = await References.Value.ConfigureAwait(false);
        var ownReferences = unit.IsTests
            ? await TestReferences.Value.ConfigureAwait(false)
            : references;

        if (unit.App.Count > 0)
        {
            // The project it references: its own diagnostics belong to the app unit, which reports
            // them. It grants the tests its internals, as InternalsVisibleTo does.
            var app = await CompileAsync(
                    AppAssemblyName,
                    unit.Page,
                    unit.App,
                    ImplicitUsings,
                    references,
                    $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{TestsAssemblyName}\")]"
                )
                .ConfigureAwait(false);
            return Report(
                await CompileAsync(
                        unit.IsTests ? TestsAssemblyName : $"{AppAssemblyName}.{unit.Group}",
                        unit.Page,
                        unit.Fences,
                        unit.IsTests ? TestImplicitUsings : ImplicitUsings,
                        [.. ownReferences, app.ToMetadataReference()],
                        withContext: false
                    )
                    .ConfigureAwait(false)
            );
        }

        return Report(
            await CompileAsync(
                    AppAssemblyName,
                    unit.Page,
                    unit.Fences,
                    ImplicitUsings,
                    ownReferences
                )
                .ConfigureAwait(false)
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

    private static async Task<CSharpCompilation> CompileAsync(
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
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"#line {fence.FirstCodeLine} \"{fence.Page}\"\n{fence.Code}\n"
                    ),
                    ParseOptions,
                    path: fence.Page
                )
            );

        // The context files join the project; a test project sees them through its reference.
        foreach (var context in withContext ? ContextFilesFor(page) : [])
            trees.Add(
                CSharpSyntaxTree.ParseText(
                    await File.ReadAllTextAsync(context).ConfigureAwait(false),
                    ParseOptions,
                    path: RepoRoot.Relative(context).Replace('\\', '/')
                )
            );

        var executable = trees.Exists(t =>
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
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{span.Path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}"
            )
            : "(no location)";
        var severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        return $"{where}: {severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    private static async Task<IReadOnlyList<MetadataReference>> LoadReferencesAsync(bool tests)
    {
        var list = Path.Combine(AppContext.BaseDirectory, "snippet-references.txt");
        if (!File.Exists(list))
            throw new InvalidOperationException(
                $"'{list}' is missing. The WriteSnippetReferences target in "
                    + "Trax.Docs.Snippets.Tests.csproj writes it on build; build the project first."
            );

        return (await File.ReadAllLinesAsync(list).ConfigureAwait(false))
            .Where(line =>
                line.Length > 0
                && (
                    !TestOnlyAssemblyPrefixes.Any(prefix => StartsWith(line, prefix))
                    || (
                        tests
                        && TestFrameworkAssemblyPrefixes.Any(prefix => StartsWith(line, prefix))
                    )
                )
            )
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
