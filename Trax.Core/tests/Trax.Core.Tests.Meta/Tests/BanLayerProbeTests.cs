using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Trax.Core.Extensions;

namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// Each ban of the team's layer fires: the analyzers the profile pins, given the repository's
/// ban files and severities, report an error on code that breaks the ban.
///
/// <para>
/// A ban is a line in a text file, and a line that names nothing reports nothing. A misspelt
/// symbol id or syntax kind, or an analyzer upgrade that reads the file differently, would turn
/// a ban off without failing a build. This compiles the offence and requires the diagnostic.
/// </para>
///
/// <para>Enforces <c>Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md"
)]
[TestFixture]
public partial class BanLayerProbeTests
{
    private const string Adr =
        "Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md";

    [TestCase("RS0030", "var now = System.DateTime.UtcNow;")]
    [TestCase("RS0030", "var now = System.DateTimeOffset.Now;")]
    [TestCase("RS0030", "var roll = System.Random.Shared.Next();")]
    [TestCase("RS0030", "var dice = new System.Random();")]
    [TestCase("RS0030", "System.Console.WriteLine(1);")]
    [TestCase("RS0030", "System.Environment.Exit(1);")]
    [TestCase("MA0240", "string? maybe = null; var length = maybe!.Length;")]
    [TestCase("MA0240", "string text = null!;")]
    [TestCase("MA0191", "string text = null!;")]
    [TestCase("MA0191", "string text = default!;")]
    public async Task ABrokenBan_IsReportedAsAnError(string rule, string statement)
    {
        var diagnostics = await Analyze(statement).ConfigureAwait(false);

        diagnostics
            .Where(d =>
                string.Equals(d.Id, rule, StringComparison.Ordinal)
                && d.Severity == DiagnosticSeverity.Error
            )
            .Should()
            .NotBeEmpty(
                $"`{statement}` breaks a ban of the analyzer profile's layer, so {rule} must "
                    + "report it as an error. Check the entry in BannedSymbols.txt or "
                    + $"BannedSyntaxes.txt and the rule's severity in .globalconfig. See {Adr}. "
                    + "Reported: "
                    + string.Join(", ", diagnostics.Select(d => $"{d.Id} ({d.Severity})"))
            );
    }

    [TestCase("var now = System.TimeProvider.System.GetUtcNow();")]
    [TestCase("string? maybe = null; var length = maybe?.Length ?? 0;")]
    [TestCase("var different = 1 != 2;")]
    public async Task CodeThatKeepsTheBans_IsNotReported(string statement)
    {
        var diagnostics = await Analyze(statement).ConfigureAwait(false);

        diagnostics
            .Where(d => d.Id is "RS0030" or "MA0240" or "MA0191")
            .Should()
            .BeEmpty($"`{statement}` breaks no ban of the layer. See {Adr}.");
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string statement)
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

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(source)],
            PlatformReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: ProfileSeverities.Value
            )
        );

        var options = new AnalyzerOptions([
            new RepositoryFile("BannedSymbols.txt"),
            new RepositoryFile("BannedSyntaxes.txt"),
        ]);

        return await compilation
            .WithAnalyzers(Analyzers.Value, options)
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences = new(() =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES").NotNull())
                .Split(Path.PathSeparator)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ]
    );

    // The severities the build applies, read from the generated profile.
    private static readonly Lazy<ImmutableDictionary<string, ReportDiagnostic>> ProfileSeverities =
        new(() =>
            Severity
                .Matches(File.ReadAllText(MonorepoRoot.Combine(".globalconfig")))
                .ToImmutableDictionary(
                    match => match.Groups["rule"].Value,
                    match =>
                        match.Groups["severity"].Value switch
                        {
                            "error" => ReportDiagnostic.Error,
                            "none" => ReportDiagnostic.Suppress,
                            _ => ReportDiagnostic.Hidden,
                        },
                    StringComparer.Ordinal
                )
        );

    // The analyzers of the two packages the layer uses, at the versions the repository pins.
    private static readonly Lazy<ImmutableArray<DiagnosticAnalyzer>> Analyzers = new(() =>
        [
            .. new[]
            {
                AnalyzerAssembly("Meziantou.Analyzer", "Meziantou.Analyzer.dll"),
                AnalyzerAssembly(
                    "Microsoft.CodeAnalysis.BannedApiAnalyzers",
                    "Microsoft.CodeAnalysis.CSharp.BannedApiAnalyzers.dll"
                ),
            }
                .SelectMany(path => Assembly.LoadFrom(path).GetTypes())
                .Where(type =>
                    !type.IsAbstract
                    && typeof(DiagnosticAnalyzer).IsAssignableFrom(type)
                    && type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is { } attribute
                    && attribute.Languages.Contains(LanguageNames.CSharp, StringComparer.Ordinal)
                )
                .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type).NotNull()),
        ]
    );

    // The package's C# analyzer assembly in the NuGet cache. A package that ships one build per
    // compiler version keeps them in folders named for it; the last sorts highest.
    private static string AnalyzerAssembly(string package, string file)
    {
        var pin = XDocument
            .Load(MonorepoRoot.Combine("Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Single(e =>
                string.Equals((string?)e.Attribute("Include"), package, StringComparison.Ordinal)
            );

        var packages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );

        var root = Path.Combine(
            packages,
            package.ToLowerInvariant(),
            (string)pin.Attribute("Version").NotNull(),
            "analyzers"
        );

        return Directory
            .EnumerateFiles(root, file, SearchOption.AllDirectories)
            .Where(path =>
                path.Contains(
                    $"{Path.DirectorySeparatorChar}cs{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .Order(StringComparer.Ordinal)
            .Last();
    }

    private sealed class RepositoryFile(string name) : AdditionalText
    {
        public override string Path { get; } = MonorepoRoot.Combine(name);

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(
            CancellationToken cancellationToken = default
        ) => Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(Path));
    }

    [GeneratedRegex(
        @"^dotnet_diagnostic\.(?<rule>[A-Z]+\d+)\.severity\s*=\s*(?<severity>\w+)\s*$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex Severity { get; }
}
