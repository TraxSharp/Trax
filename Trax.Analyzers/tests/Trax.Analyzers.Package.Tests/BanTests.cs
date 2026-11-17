using System.Collections.Immutable;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Trax.Analyzers.Package.Tests;

/// <summary>
/// Every line of every ban file the package ships bans something. A ban is a line of text, and a
/// line that names nothing (a misspelt documentation id, a syntax kind that does not exist)
/// reports nothing and fails no build; so each line has a probe here that breaks it, and the probe
/// must be reported as an error.
/// </summary>
[TestFixture]
public class BanTests
{
    // The documentation id or syntax kind on each ban line, and a statement that breaks it.
    private static readonly IReadOnlyDictionary<string, string> Probes = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["P:System.DateTime.Now"] = "var x = System.DateTime.Now;",
        ["P:System.DateTime.UtcNow"] = "var x = System.DateTime.UtcNow;",
        ["P:System.DateTime.Today"] = "var x = System.DateTime.Today;",
        ["P:System.DateTimeOffset.Now"] = "var x = System.DateTimeOffset.Now;",
        ["P:System.DateTimeOffset.UtcNow"] = "var x = System.DateTimeOffset.UtcNow;",
        ["P:System.TimeProvider.System"] = "var x = System.TimeProvider.System.GetUtcNow();",
        ["P:System.Environment.TickCount"] = "var x = System.Environment.TickCount;",
        ["P:System.Environment.TickCount64"] = "var x = System.Environment.TickCount64;",
        ["M:System.Diagnostics.Stopwatch.GetTimestamp"] =
            "var x = System.Diagnostics.Stopwatch.GetTimestamp();",
        ["M:System.Diagnostics.Stopwatch.StartNew"] =
            "var x = System.Diagnostics.Stopwatch.StartNew();",
        ["M:System.Diagnostics.Stopwatch.#ctor"] = "var x = new System.Diagnostics.Stopwatch();",
        ["P:System.Random.Shared"] = "var x = System.Random.Shared.Next();",
        ["M:System.Random.#ctor"] = "var x = new System.Random();",
        ["T:System.Console"] = "System.Console.WriteLine(1);",
        ["M:System.Environment.Exit(System.Int32)"] = "System.Environment.Exit(1);",
        ["M:System.Environment.FailFast(System.String)"] = "System.Environment.FailFast(\"x\");",
        ["M:System.Environment.FailFast(System.String,System.Exception)"] =
            "System.Environment.FailFast(\"x\", new System.Exception());",
        ["SuppressNullableWarningExpression"] = "string? maybe = null; var length = maybe!.Length;",
    };

    public static IEnumerable<TestCaseData> BanLines() =>
        Directory
            .EnumerateFiles(Folder.Bans, "*.txt")
            .Order(StringComparer.Ordinal)
            .SelectMany(file =>
                File.ReadAllLines(file)
                    .Where(line =>
                        line.Trim() is { Length: > 0 } text
                        && !text.StartsWith("//", StringComparison.Ordinal)
                        && !text.StartsWith('#')
                    )
                    .Select(line => new TestCaseData(Path.GetFileName(file), line.Split(';')[0]))
            );

    [TestCaseSource(nameof(BanLines))]
    public async Task EveryBanLine_IsReportedAsAnError(string file, string banned)
    {
        Probes
            .Should()
            .ContainKey(banned, $"{file} bans {banned}; give it a probe in {nameof(Probes)}");

        var rule = file.StartsWith("BannedSyntaxes", StringComparison.Ordinal)
            ? "TRX0240"
            : "RS0030";
        var diagnostics = await Analyze(Probes[banned], file).ConfigureAwait(false);

        diagnostics
            .Where(d =>
                string.Equals(d.Id, rule, StringComparison.Ordinal)
                && d.Severity == DiagnosticSeverity.Error
            )
            .Should()
            .NotBeEmpty(
                $"`{Probes[banned]}` breaks the ban on {banned} in {file}, so {rule} must report it. "
                    + "Reported: "
                    + string.Join(", ", diagnostics.Select(d => $"{d.Id} ({d.Severity})"))
            );
    }

    [TestCase("string text = null!;")]
    [TestCase("string text = default!;")]
    public async Task NullAndDefaultForgiven_AreTRX0191(string statement)
    {
        var diagnostics = await Analyze(statement, banFile: null).ConfigureAwait(false);

        diagnostics
            .Where(d =>
                string.Equals(d.Id, "TRX0191", StringComparison.Ordinal)
                && d.Severity == DiagnosticSeverity.Error
            )
            .Should()
            .NotBeEmpty();
    }

    [TestCase("var x = new System.Random(42).Next();")]
    [TestCase("var x = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10);")]
    [TestCase("string? maybe = null; var length = maybe?.Length ?? 0;")]
    [TestCase("System.TimeProvider clock = null!; var x = clock.GetUtcNow();")]
    public async Task CodeThatKeepsTheBans_IsNotReportedByThem(string statement)
    {
        var all = Directory.EnumerateFiles(Folder.Bans, "BannedSymbols.*.txt").ToArray();
        var diagnostics = await Analyze(statement, all).ConfigureAwait(false);

        diagnostics.Where(d => d.Id is "RS0030").Should().BeEmpty();
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(string statement, string? banFile) =>
        Analyze(statement, banFile is null ? [] : [Path.Combine(Folder.Bans, banFile)]);

    private static async Task<ImmutableArray<Diagnostic>> Analyze(
        string statement,
        string[] banFiles
    )
    {
        var source = $$"""
            #nullable enable
            public static class Probe
            {
                public static void Run()
                {
                    {{statement}}
                }
            }
            """;

        var severities = File.ReadAllLines(Folder.Profile("1", "src"))
            .Where(line => line.StartsWith("dotnet_diagnostic.", StringComparison.Ordinal))
            .Select(line => line.Split(['.', ' '], StringSplitOptions.RemoveEmptyEntries))
            .ToImmutableDictionary(
                parts => parts[1],
                parts =>
                    parts[^1] switch
                    {
                        "error" => ReportDiagnostic.Error,
                        "none" => ReportDiagnostic.Suppress,
                        _ => ReportDiagnostic.Hidden,
                    },
                StringComparer.Ordinal
            );

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source)],
            PlatformReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: severities
            )
        );

        var options = new AnalyzerOptions([
            .. banFiles.Select(f => (AdditionalText)new FileText(f)),
        ]);

        return await compilation
            .WithAnalyzers(Analyzers.Value, options)
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences = new(() =>
        [
            .. (
                (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("No trusted platform assemblies")
            )
                .Split(Path.PathSeparator)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ]
    );

    // The rules (this folder's build) and the banned-API analyzer (the version the package
    // depends on).
    private static readonly Lazy<ImmutableArray<DiagnosticAnalyzer>> Analyzers = new(() =>
        [
            .. new[]
            {
                Assembly.Load("Trax.Analyzers"),
                Assembly.LoadFrom(
                    Directory
                        .EnumerateFiles(
                            Path.Combine(Folder.BannedApiAnalyzers, "analyzers", "dotnet", "cs"),
                            "Microsoft.CodeAnalysis.CSharp.BannedApiAnalyzers.dll"
                        )
                        .Single()
                ),
            }
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type =>
                    !type.IsAbstract
                    && typeof(DiagnosticAnalyzer).IsAssignableFrom(type)
                    && type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is { } attribute
                    && attribute.Languages.Contains(LanguageNames.CSharp, StringComparer.Ordinal)
                )
                .Select(type =>
                    (DiagnosticAnalyzer)(
                        Activator.CreateInstance(type)
                        ?? throw new InvalidOperationException($"Cannot create {type}")
                    )
                ),
        ]
    );

    private sealed class FileText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(File.ReadAllText(Path));
    }
}
