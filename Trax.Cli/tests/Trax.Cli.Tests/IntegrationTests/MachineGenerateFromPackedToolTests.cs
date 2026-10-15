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

    // An outside developer installs both packages and nothing else of this repository: the engine from its npm tarball
    // and the tool from its nupkg. A break that shows only in what is published (an export-map entry, a file left out
    // of the tarball) fails here instead of in the next outside developer's build.
    [Test]
    public void The_published_engine_and_tool_typecheck_together_in_an_empty_project()
    {
        var tool = PackAndUnpackTheTool();
        var engine = RepoFolder("Trax.Api.StateMachine");
        var (packed, packOutput) = Run("npm", _root, "pack", engine, "--pack-destination", _root);
        packed.Should().Be(0, packOutput);
        var tarball = Directory.GetFiles(_root, "traxsharp-state-machine-*.tgz").Single();

        var work = Path.Combine(_root, "outside");
        Directory.CreateDirectory(work);
        File.WriteAllText(
            Path.Combine(work, "package.json"),
            """{ "name": "outside", "private": true, "type": "module" }"""
        );
        var (installed, installOutput) = Run(
            "npm",
            work,
            "install",
            "--no-audit",
            "--no-fund",
            tarball,
            $"typescript@{EngineDevDependency(engine, "typescript")}",
            $"react@{EngineDevDependency(engine, "react")}",
            $"@types/react@{EngineDevDependency(engine, "@types/react")}",
            "graphql-ws@6"
        );
        installed.Should().Be(0, installOutput);

        var (generated, generateOutput) = Run(
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
            "twin"
        );
        generated.Should().Be(0, generateOutput);

        // The client: generated by the published tool from a manifest, as a frontend's build generates it.
        File.WriteAllText(
            Path.Combine(work, "manifest.json"),
            Trax.Effect.StateMachine.ClientManifest.Write(Fakes.ClientManifests.Sample())
        );
        var (clientCode, clientOutput) = Run(
            "dotnet",
            work,
            Path.Combine(tool, "trax.dll"),
            "client",
            "--manifest",
            "manifest.json",
            "--out",
            "trax"
        );
        clientCode.Should().Be(0, clientOutput);
        var (clientCheckCode, clientCheckOutput) = Run(
            "dotnet",
            work,
            Path.Combine(tool, "trax.dll"),
            "client",
            "--manifest",
            "manifest.json",
            "--out",
            "trax",
            "--check"
        );
        clientCheckCode
            .Should()
            .Be(0, "the client just generated is what the manifest generates: " + clientCheckOutput);
        File.WriteAllText(
            Path.Combine(work, "page.ts"),
            """
            import { configureTrax, useReminder, startRun, type StartRunInput } from "./trax/index.g";

            configureTrax({ url: "/graphql", socketUrl: "ws://localhost/graphql" });

            export function usePage() {
              const reminder = useReminder({ startInput: {} });
              void reminder.send("Approve");
              // @ts-expect-error a timer trigger is the server's to fire, never a client's send
              void reminder.send("Review.after");
              return reminder.state;
            }

            const input: StartRunInput = { scenario: "crash" };
            export const run = () => startRun(input, { mode: "QUEUE" });
            """
        );

        File.WriteAllText(
            Path.Combine(work, "consumer.ts"),
            """
            import { requireClientFormat, clientFormat, RefusalCode } from "@traxsharp/state-machine";
            import { createMachineExecutor } from "@traxsharp/state-machine/client";
            import { turnstile } from "./twin/turnstile.machine.g";

            requireClientFormat(clientFormat);
            const code: RefusalCode = RefusalCode.GuardFailed;
            export const state: string = turnstile.initial().state;
            export const make = () => createMachineExecutor("/graphql", { machines: [{ machine: turnstile, documents: [] }] });
            export { code };
            """
        );
        File.WriteAllText(
            Path.Combine(work, "tsconfig.json"),
            """
            {
              "compilerOptions": {
                "strict": true, "noEmit": true, "target": "ES2022", "module": "ESNext",
                "moduleResolution": "bundler", "skipLibCheck": false, "lib": ["ES2022", "DOM"]
              },
              "include": ["consumer.ts", "page.ts", "twin/*.ts", "trax/**/*.ts"]
            }
            """
        );
        var (checkedCode, checkOutput) = Run(
            "node",
            work,
            Path.Combine(work, "node_modules", "typescript", "bin", "tsc"),
            "-p",
            "tsconfig.json"
        );
        checkedCode.Should().Be(0, checkOutput);
    }

    // A dev dependency's version as the engine pins it, so the outside project typechecks with the TypeScript the
    // engine is built with.
    private static string EngineDevDependency(string engine, string name) =>
        System
            .Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(engine, "package.json")))
            .RootElement.GetProperty("devDependencies")
            .GetProperty(name)
            .GetString()!
            .TrimStart('^', '~');

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
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
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
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        // Bounded, so a hung child fails this test with what it printed instead of stalling the run.
        var exited = process.WaitForExit(TimeSpan.FromMinutes(5));
        if (!exited)
            process.Kill(entireProcessTree: true);
        Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(30));
        var output = Drained(stdout) + Drained(stderr);
        return exited
            ? (process.ExitCode, output)
            : (-1, $"{fileName} {string.Join(' ', args)} timed out after five minutes:\n{output}");
    }

    private static string Drained(Task<string> read) =>
        read.IsCompletedSuccessfully ? read.Result : "(output still held open by a child process)";
}
