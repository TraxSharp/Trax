using System.Diagnostics;
using System.IO.Compression;
using AwesomeAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// The installed tool generates a twin and a corpus on its own: packed, unpacked into a folder of its own and
/// run from an empty folder with no <c>--engine-src</c>, it needs nothing but node. The engine and the
/// generators ship inside the package, bundled to plain JavaScript when it is packed. The corpus it writes is
/// then replayed through the C# engine, which must agree with it case for case.
///
/// <para>Packing needs node and the engine's npm packages (esbuild bundles it), so this is skipped where either
/// is missing, except under CI, which installs both for this folder and fails the test instead.</para>
/// </summary>
public class MachineGenerateFromPackedToolTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        if (!new NodeRunner().IsAvailable())
            NodePrerequisite.Missing(
                "node is not on PATH; the packed tool's twin and corpus need it."
            );
        if (
            !Directory.Exists(
                Path.Combine(RepoFolder("Trax.Api.StateMachine"), "node_modules", "esbuild")
            )
        )
            NodePrerequisite.Missing(
                "Trax.Api.StateMachine has no node_modules; run `npm ci` there to pack the tool."
            );

        _root = Path.Combine(Path.GetTempPath(), $"trax-packed-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (_root is not null && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void The_packed_tool_generates_a_twin_and_a_corpus_the_CSharp_engine_replays_without_engine_src()
    {
        var tool = PackAndUnpackTheTool();
        var work = Path.Combine(_root, "consumer");
        Directory.CreateDirectory(work);

        var (exitCode, output) = Run(
            "dotnet",
            work,
            Path.Combine(tool, "trax.dll"),
            "machine",
            "generate",
            "--assembly",
            typeof(DeclarativeTurnstileMachine).Assembly.Location,
            "--machine",
            typeof(DeclarativeTurnstileMachine).FullName!,
            "--twin-out",
            "twin",
            "--corpus-out",
            "corpus"
        );

        exitCode.Should().Be(0, output);
        // With no engine checkout to import by path, the twin imports the published package.
        File.ReadAllText(Path.Combine(work, "twin", "turnstile.machine.g.ts"))
            .Should()
            .Contain("from \"@traxsharp/state-machine\"");
        File.ReadAllText(Path.Combine(work, "twin", "turnstile.contexts.g.ts"))
            .Should()
            .Contain("export type TurnstileState");

        var corpus = File.ReadAllText(Path.Combine(work, "corpus", "differential.json"));
        corpus.Should().Contain("\"cases\"");
        new TurnstileReplaying(corpus).SelfCheck().Should().BeEmpty();
    }

    // The turnstile, shipping the corpus the tool just wrote, so SelfCheck replays it through the C# engine.
    // Its constructor takes the corpus, which keeps the CLI's machine loader from picking it up.
    private sealed class TurnstileReplaying(string corpus) : DeclarativeTurnstileMachine
    {
        public override string? Corpus => corpus;
    }

    // Packs Trax.Cli as it was built for this test run, and unpacks the tool the way `dotnet tool install` does.
    // The solution, not the project, as CI packs it: a solution build leaves the referenced Trax.Effect
    // projects, which are outside it, at their default configuration, and a project pack would look for them
    // at this one.
    private string PackAndUnpackTheTool()
    {
        var project = Path.Combine(RepoFolder("Trax.Cli"), "Trax.Cli.slnx");
        var packages = Path.Combine(_root, "packages");
        var (exitCode, output) = Run(
            "dotnet",
            _root,
            "pack",
            project,
            "--no-build",
            "--configuration",
            BuildConfiguration(),
            "--output",
            packages,
            "-p:Version=0.0.0-packtest",
            "-p:EnablePackageValidation=false"
        );
        exitCode.Should().Be(0, output);

        var unpacked = Path.Combine(_root, "tool");
        ZipFile.ExtractToDirectory(Directory.GetFiles(packages, "*.nupkg").Single(), unpacked);
        return Path.Combine(unpacked, "tools", "net10.0", "any");
    }

    // bin/<Configuration>/<tfm>/: the configuration this test run built, which pack --no-build must match.
    private static string BuildConfiguration() =>
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
            .Parent!
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

    private static (int ExitCode, string Output) Run(
        string fileName,
        string workingDirectory,
        params string[] args
    )
    {
        var psi = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        var (exitCode, stdout, stderr) = ChildProcess.Run(psi);
        return (exitCode, stdout + stderr);
    }
}
