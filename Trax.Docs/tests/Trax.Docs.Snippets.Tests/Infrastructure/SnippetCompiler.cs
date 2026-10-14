using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// The fences of one page that compile together: one fence marked <c>compile</c>, or every fence
/// on the page that names the same <c>compile=group</c>.
/// </summary>
public sealed record SnippetUnit(string Page, string Group, IReadOnlyList<Fence> Fences)
{
    public override string ToString() => $"{Page} [{Group}]";

    /// <summary>The compiled fences of every page, one unit per page and group.</summary>
    public static IEnumerable<SnippetUnit> InDocs() =>
        Infrastructure
            .Fences.InDocs()
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
        "Microsoft.CodeAnalysis",
        "Microsoft.TestPlatform",
        "Microsoft.VisualStudio.TestPlatform",
        "Microsoft.VisualStudio.CodeCoverage",
        "testhost",
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

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(LoadReferences);

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

    /// <summary>
    /// The unit's errors and warnings, each as <c>page.md:line:column: severity id: message</c>.
    /// Empty when it compiles cleanly.
    /// </summary>
    public static IReadOnlyList<string> Diagnose(SnippetUnit unit)
    {
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                string.Concat(ImplicitUsings.Select(u => $"global using global::{u};\n")),
                ParseOptions,
                path: "ImplicitUsings.g.cs"
            ),
        };

        foreach (var fence in unit.Fences)
            trees.Add(
                CSharpSyntaxTree.ParseText(
                    $"#line {fence.FirstCodeLine} \"{fence.Page}\"\n{fence.Code}\n",
                    ParseOptions,
                    path: fence.Page
                )
            );

        foreach (var context in ContextFilesFor(unit.Page))
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

        var compilation = CSharpCompilation.Create(
            "Snippet",
            trees,
            References.Value,
            new CSharpCompilationOptions(
                executable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: MSBuildNoWarn
            )
        );

        return compilation
            .GetDiagnostics()
            .Where(d =>
                !d.IsSuppressed
                && d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning
            )
            .OrderBy(d => d.Location.GetMappedLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location.GetMappedLineSpan().StartLinePosition.Line)
            .Select(Format)
            .ToList();
    }

    private static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetMappedLineSpan();
        var where = span.IsValid
            ? $"{span.Path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}"
            : "(no location)";
        var severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
        return $"{where}: {severity} {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    private static IReadOnlyList<MetadataReference> LoadReferences()
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
                !TestOnlyAssemblyPrefixes.Any(prefix =>
                    Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                )
            )
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
