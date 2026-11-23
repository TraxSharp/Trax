using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Core.Extensions;

namespace Trax.Api.Tests.Dashboard;

/// <summary>
/// The package carries the built app: packed as CI packs it, its assembly holds the app's
/// <c>index.html</c> and its content-hashed scripts and styles, so a host that installs it needs
/// no Node.
///
/// <para>Packing needs the app built into the assembly, which needs node and the app's npm
/// packages, so this is skipped on a developer's machine where either is missing. CI installs both
/// for this folder, so there it fails instead.</para>
/// </summary>
public partial class ApiDashboardPackageTests
{
    private string? _root;

    [SetUp]
    public async Task SetUp()
    {
        var node = await RunAsync("node", Path.GetTempPath(), "--version");
        if (node.ExitCode != 0)
            Unavailable("node is not on PATH; packing Trax.Api.Dashboard builds its app with it.");
        if (!Directory.Exists(Path.Combine(ClientApp(), "node_modules", "vite")))
            Unavailable(
                "Trax.Api/src/Trax.Api.Dashboard/ClientApp has no node_modules; run `npm ci` there to pack it."
            );

        _root = Path.Combine(Path.GetTempPath(), $"trax-api-dashboard-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root.NotNull());
    }

    // Skips on a developer's machine; fails under CI, which installs node and the app's packages
    // for this folder, so a skip there would report green without ever packing the app.
    private static void Unavailable(string why)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            Assert.Fail($"{why} CI installs both for Trax.Api, so this test must run there.");
        Assert.Ignore(why);
    }

    [TearDown]
    public void TearDown()
    {
        if (_root is not null && Directory.Exists(_root.NotNull()))
            Directory.Delete(_root.NotNull(), recursive: true);
    }

    [Test]
    public async Task The_package_carries_the_built_app_with_content_hashed_assets()
    {
        var unpacked = await PackAndUnpackAsync();

        var assembly = Path.Combine(unpacked, "lib", "net10.0", "Trax.Api.Dashboard.dll");
        File.Exists(assembly).Should().BeTrue("the package ships the assembly");
        File.Exists(Path.Combine(unpacked, "README.md"))
            .Should()
            .BeTrue("every package carries a README");

        var resources = await ManifestResourcesAsync(assembly);
        resources.Should().Contain("TraxApiDashboard/index.html");
        var assets = resources
            .Where(r => r.StartsWith("TraxApiDashboard/assets/", StringComparison.Ordinal))
            .ToList();
        assets
            .Should()
            .Contain(a => HashedScript.IsMatch(a), "the app's script is named by its content");
        assets
            .Should()
            .Contain(a => HashedStyle.IsMatch(a), "the app's styles are named by their content");
        resources
            .Should()
            .NotContain(
                r => r.Contains("dashboard-recordings"),
                "the demo's recordings are not shipped"
            );
    }

    [GeneratedRegex(
        @"^TraxApiDashboard/assets/index-[A-Za-z0-9_-]{6,}\.js$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex HashedScript { get; }

    [GeneratedRegex(
        @"^TraxApiDashboard/assets/index-[A-Za-z0-9_-]{6,}\.css$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex HashedStyle { get; }

    private static async Task<List<string>> ManifestResourcesAsync(string assemblyPath)
    {
        var stream = File.OpenRead(assemblyPath);
        await using var streamDisposal = stream.ConfigureAwait(false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata
            .ManifestResources.Select(h => metadata.GetString(metadata.GetManifestResource(h).Name))
            .ToList();
    }

    // Packs the project as this test run built it, the way CI packs after its build. Package
    // validation is off: it reads the reference assemblies of the projects this one references,
    // and a build of Trax.Api.slnx builds the ones outside the solution (Trax.Effect and the other
    // upstream folders) in their default configuration, not this one, so a pack of the project on
    // its own looks for files that were never built. CI's pack of the solution validates the
    // package; this test checks what it carries.
    private async Task<string> PackAndUnpackAsync()
    {
        var project = Path.Combine(
            RepoFolder("Trax.Api"),
            "src",
            "Trax.Api.Dashboard",
            "Trax.Api.Dashboard.csproj"
        );
        var packages = Path.Combine(_root.NotNull(), "packages");
        var (exitCode, output) = await RunAsync(
                "dotnet",
                _root.NotNull(),
                "pack",
                project,
                "--no-build",
                "--configuration",
                BuildConfiguration(),
                "--output",
                packages,
                "-p:Version=0.0.0-packtest",
                "-p:EnablePackageValidation=false"
            )
            .ConfigureAwait(false);
        exitCode.Should().Be(0, output);

        var unpacked = Path.Combine(_root.NotNull(), "package");
        await ZipFile
            .ExtractToDirectoryAsync(
                Directory.GetFiles(packages, "*.nupkg").Single(),
                unpacked,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        return unpacked;
    }

    private static string ClientApp() =>
        Path.Combine(RepoFolder("Trax.Api"), "src", "Trax.Api.Dashboard", "ClientApp");

    // bin/<Configuration>/<tfm>/: the configuration this test run built, which pack --no-build must match.
    private static string BuildConfiguration() =>
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
            .Parent.NotNull()
            .Name;

    private static string RepoFolder(string name)
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (
                Directory.Exists(candidate)
                && File.Exists(Path.Combine(dir.FullName, "global.json"))
            )
                return candidate;
        }
        throw new InvalidOperationException($"No {name} folder above {AppContext.BaseDirectory}.");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        string workingDirectory,
        params string[] args
    )
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        // A pack runs MSBuild worker nodes that would otherwise outlive it, holding this process's
        // output pipes open for their idle timeout, so reading the output would hang long after the
        // pack had finished. The test host's MSBuild environment must not leak into the child either.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment.Remove("MSBuildSDKsPath");
        psi.Environment.Remove("MSBuildExtensionsPath");
        psi.Environment.Remove("MSBUILD_EXE_PATH");
        try
        {
            using var process = Process.Start(psi).NotNull();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            // Under the blame collector's five minutes, so a hung child fails this test with what
            // it printed instead of the collector killing the whole test host.
            var exited = process.WaitForExit(TimeSpan.FromMinutes(4));
            if (!exited)
                process.Kill(entireProcessTree: true);
            Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(30));
            var output =
                await DrainedAsync(stdout).ConfigureAwait(false)
                + await DrainedAsync(stderr).ConfigureAwait(false);
            return exited
                ? (process.ExitCode, output)
                : (
                    -1,
                    $"{fileName} {string.Join(' ', args)} timed out after four minutes:\n{output}"
                );
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, e.Message);
        }
    }

    private static async Task<string> DrainedAsync(Task<string> read) =>
        read.IsCompletedSuccessfully
            ? await read.ConfigureAwait(false)
            : "(output still held open by a child process)";
}
