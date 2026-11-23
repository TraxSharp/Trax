using System.Diagnostics;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Trax.Analyzers.Package.Tests;

/// <summary>
/// The props the package ships do what they say, in a real build: a project that imports them gets
/// the profile and the bans, a test project gets the test profile, and each switch turns off what it
/// names. The other tests hand the analyzers their inputs directly; only a build sees the wiring.
/// </summary>
[TestFixture]
[NonParallelizable]
public partial class WiringTests
{
    // Breaks a vendor rule (TRX0004), a ban (Console, RS0030) and the null-forgiving ban (TRX0240).
    private const string Offences = """
        #nullable enable
        public static class Probe
        {
            public static async System.Threading.Tasks.Task Run(string? maybe)
            {
                await System.Threading.Tasks.Task.Delay(1);
                System.Console.WriteLine(maybe!.Length);
            }
        }
        """;

    [Test]
    public void AProject_GetsTheProfileAndTheBans()
    {
        var errors = Build();

        errors.Should().Contain(["TRX0004", "RS0030", "TRX0240"]);
    }

    [Test]
    public void ATestProject_GetsTheTestProfile()
    {
        var errors = Build(("IsTestProject", "true"));

        errors.Should().Contain(["TRX0004", "RS0030", "TRX0240"]);
    }

    [Test]
    public void TraxAnalyzersTests_False_LeavesTestProjectsOut()
    {
        var errors = Build(("IsTestProject", "true"), ("TraxAnalyzersTests", "false"));

        errors
            .Should()
            .NotContain(e => e.StartsWith("TRX", StringComparison.Ordinal) || e == "RS0030");
    }

    [Test]
    public void TraxAnalyzers_False_TurnsTheWholeProfileOff()
    {
        var errors = Build(("TraxAnalyzers", "false"));

        errors
            .Should()
            .NotContain(e => e.StartsWith("TRX", StringComparison.Ordinal) || e == "RS0030");
    }

    [Test]
    public void ABanSwitch_TurnsOffOnlyItsBan()
    {
        var errors = Build(("TraxBanConsole", "false"), ("TraxBanNullForgiving", "false"));

        errors.Should().Contain("TRX0004").And.NotContain(["RS0030", "TRX0240"]);
    }

    [Test]
    public void AnEditorConfigLine_TurnsOneRuleOff()
    {
        var errors = Build(editorConfig: "[*.cs]\ndotnet_diagnostic.TRX0004.severity = none\n");

        errors.Should().NotContain("TRX0004").And.Contain("RS0030");
    }

    private static HashSet<string> Build(params (string Name, string Value)[] properties) =>
        Build(editorConfig: null, properties);

    private static HashSet<string> Build(
        string? editorConfig,
        params (string Name, string Value)[] properties
    )
    {
        var dir = Directory.CreateTempSubdirectory("trax-analyzers-probe-");
        try
        {
            var props = Path.Combine(Folder.Package, "build", "Trax.Analyzers.props");
            var rules = typeof(Folder).Assembly.Location.Replace(
                "Trax.Analyzers.Package.Tests.dll",
                "Trax.Analyzers.dll",
                StringComparison.Ordinal
            );
            var banned = Directory
                .EnumerateFiles(
                    Path.Combine(Folder.BannedApiAnalyzers, "analyzers", "dotnet", "cs"),
                    "*.dll"
                )
                .Select(path => $"""<Analyzer Include="{path}" />""");

            File.WriteAllText(
                Path.Combine(dir.FullName, "Probe.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <NuGetAudit>false</NuGetAudit>
                    {string.Concat(properties.Select(p => $"<{p.Name}>{p.Value}</{p.Name}>"))}
                  </PropertyGroup>
                  <Import Project="{props}" />
                  <ItemGroup>
                    <Analyzer Include="{rules}" />
                    {string.Concat(banned)}
                  </ItemGroup>
                </Project>
                """
            );
            File.WriteAllText(Path.Combine(dir.FullName, "Probe.cs"), Offences);
            if (editorConfig is not null)
                File.WriteAllText(
                    Path.Combine(dir.FullName, ".editorconfig"),
                    "root = true\n" + editorConfig
                );

            var start = new ProcessStartInfo(
                "dotnet",
                ["build", "-nologo", "-clp:NoSummary", "Probe.csproj"]
            )
            {
                WorkingDirectory = dir.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process =
                Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();

            return [.. ErrorId().Matches(output).Select(m => m.Groups["id"].Value)];
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [GeneratedRegex(
        @"error (?<id>[A-Z]+[0-9]+):",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex ErrorId();
}
