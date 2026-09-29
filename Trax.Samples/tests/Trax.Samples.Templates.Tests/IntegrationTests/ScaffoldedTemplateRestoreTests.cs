using System.Diagnostics;
using FluentAssertions;

namespace Trax.Samples.Templates.Tests.IntegrationTests;

/// <summary>
/// A project scaffolded from the packed <c>Trax.Samples.Templates</c> restores on its own,
/// outside this repository. Inside the repo the template projects take their package versions
/// from the root <c>Directory.Packages.props</c>, which a scaffolded project never sees, so the
/// package has to carry the versions with it.
///
/// <para>Enforces <c>docs/adr/0004-the-template-package-carries-its-package-versions.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0004-the-template-package-carries-its-package-versions.md")]
public class ScaffoldedTemplateRestoreTests
{
    private const string Adr =
        "see docs/adr/0004-the-template-package-carries-its-package-versions.md";

    // Every process below is waited on with this ceiling, so a hung restore fails the test
    // with its output instead of running into the runner's own timeout.
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    private string _workDir = null!;
    private string _hive = null!;

    [OneTimeSetUp]
    public async Task PackAndInstallTheTemplates()
    {
        // A directory with no Directory.Packages.props, Directory.Build.props or nuget.config
        // above it, which is where a consumer runs `dotnet new`.
        _workDir = Path.Combine(Path.GetTempPath(), "trax-template-restore-" + Guid.NewGuid());
        _hive = Path.Combine(_workDir, "hive");
        var nupkgDir = Path.Combine(_workDir, "nupkg");
        Directory.CreateDirectory(_workDir);

        var templatesProject = Path.Combine(
            RepoRoot(),
            "templates",
            "Trax.Samples.Templates.csproj"
        );

        // An empty TraxLocalVersion packs the committed pins even in a workspace checkout
        // where trax-local.props points every Trax package at the local feed.
        await Run(
            _workDir,
            "pack",
            templatesProject,
            "--output",
            nupkgDir,
            "-p:Version=0.0.0-scaffold-test",
            "-p:TraxLocalVersion="
        );

        var nupkg = Directory.GetFiles(nupkgDir, "*.nupkg").Single();
        await Run(_workDir, "new", "install", nupkg, "--debug:custom-hive", _hive);
    }

    [OneTimeTearDown]
    public void DeleteTheWorkDirectory()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    [TestCase("trax-hub")]
    [TestCase("trax-scheduler")]
    [TestCase("trax-api")]
    public async Task A_scaffolded_project_restores_outside_the_repo(string shortName)
    {
        var output = Path.Combine(_workDir, shortName);
        await Run(
            _workDir,
            "new",
            shortName,
            "--name",
            "Scaffolded",
            "--output",
            output,
            "--debug:custom-hive",
            _hive
        );

        var restore = await Run(output, allowFailure: true, "restore");

        restore
            .ExitCode.Should()
            .Be(0, $"a scaffolded {shortName} must restore on its own ({Adr}):\n{restore.Output}");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trax.Samples.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Trax.Samples.slnx not found above the tests");
    }

    private static Task<(int ExitCode, string Output)> Run(
        string workingDirectory,
        params string[] args
    ) => Run(workingDirectory, allowFailure: false, args);

    private static async Task<(int ExitCode, string Output)> Run(
        string workingDirectory,
        bool allowFailure,
        params string[] args
    )
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        // Keep the child out of the test host's MSBuild environment.
        psi.Environment.Remove("MSBuildSDKsPath");
        psi.Environment.Remove("MSBuildExtensionsPath");
        psi.Environment.Remove("MSBUILD_EXE_PATH");

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet {string.Join(' ', args)} did not finish within {ProcessTimeout}"
            );
        }

        var output = await stdout + await stderr;
        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', args)} exited {process.ExitCode}:\n{output}"
            );
        return (process.ExitCode, output);
    }
}
