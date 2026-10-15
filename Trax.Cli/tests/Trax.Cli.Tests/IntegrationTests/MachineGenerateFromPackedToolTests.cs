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
        var work = OutsideProject(RepoFolder("Trax.Api.StateMachine"));

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

        GenerateClient(tool, work);
        File.WriteAllText(
            Path.Combine(work, "page.ts"),
            """
            import { configureTrax, useReminder, useRememberedCheckout, startRun, useRunProgress, followRunProgress, type StartRunInput } from "./trax/index.g";

            configureTrax({ url: "/graphql", socketUrl: "ws://localhost/graphql" });

            export function usePage() {
              const reminder = useReminder({ startInput: {} });
              void reminder.send("Approve");
              // @ts-expect-error a timer trigger is the server's to fire, never a client's send
              void reminder.send("Review.after");
              return reminder.state;
            }

            export function useDraft(user: string) {
              // @ts-expect-error a remembered instance is kept per user, so the hook needs one
              useRememberedCheckout({});
              return useRememberedCheckout({ user }).forget;
            }

            const input: StartRunInput = { scenario: "crash" };
            export const run = () => startRun(input, { mode: "QUEUE" });

            export function useRunPage(runId: string | null) {
              const progress = useRunProgress(runId);
              const names: string[] = progress.steps.map((s) => s.name);
              // @ts-expect-error a state is one of the run's states, never any string
              const wrong: typeof progress.state = "DONE";
              const attempts: string[] = [...progress.earlier, ...(progress.runId ? [progress.runId] : [])];
              return { names, attempts, done: progress.state === "COMPLETED", wrong };
            }

            export const stop = followRunProgress("r", (e) => void e.sequence);
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

    // The generated hooks run under vitest in a project that installed only the published packages, against a fake
    // graphql-ws socket and a fetch that answers when told to: what configureTrax does to a mounted hook, and what
    // useRunProgress shows when the stream and a read answer out of order.
    [Test]
    public void The_generated_hooks_behave_in_an_outside_project()
    {
        var tool = PackAndUnpackTheTool();
        var engine = RepoFolder("Trax.Api.StateMachine");
        var work = OutsideProject(
            engine,
            $"react-dom@{EngineDevDependency(engine, "react-dom")}",
            $"vitest@{EngineDevDependency(engine, "vitest")}",
            $"jsdom@{EngineDevDependency(engine, "jsdom")}",
            $"@testing-library/react@{EngineDevDependency(engine, "@testing-library/react")}"
        );
        GenerateClient(tool, work);
        File.WriteAllText(
            Path.Combine(work, "vitest.config.mjs"),
            """
            export default { test: { include: ["client.g.test.ts"], environment: "jsdom" } };
            """
        );
        File.WriteAllText(Path.Combine(work, "client.g.test.ts"), GeneratedHookTests);

        var (code, output) = Run(
            "node",
            work,
            Path.Combine(work, "node_modules", "vitest", "vitest.mjs"),
            "run"
        );

        code.Should().Be(0, output);
    }

    private const string GeneratedHookTests = """
        import { act, cleanup, renderHook } from "@testing-library/react";
        import { afterEach, expect, it, vi } from "vitest";

        interface Subscription { payload: { id: string; variables: Record<string, unknown> }; sink: { next(v: unknown): void; complete(): void }; closed: boolean }
        interface Socket { params: () => unknown; subscriptions: Subscription[]; disposed: boolean }

        // Each socket the client opens: what it would send in connection_init, and its subscriptions. Disposing it ends
        // each open subscription, as closing a graphql-ws socket does.
        const sockets = vi.hoisted(() => [] as Socket[]);
        vi.mock("graphql-ws", () => ({
          createClient: (options: { connectionParams?: () => unknown }) => {
            const socket: Socket = { params: options.connectionParams ?? (() => undefined), subscriptions: [], disposed: false };
            sockets.push(socket);
            return {
              subscribe(payload: Subscription["payload"], sink: Subscription["sink"]) {
                const subscription = { payload, sink, closed: false };
                socket.subscriptions.push(subscription);
                return () => { subscription.closed = true; };
              },
              async dispose() {
                socket.disposed = true;
                for (const s of socket.subscriptions) if (!s.closed) { s.closed = true; s.sink.complete(); }
              },
            };
          },
        }));

        import { configureTrax, reminderServer, useReminder, useRunProgress } from "./trax/index.g";

        // Every request, with its headers, answered only when the test says so.
        const requests: { id: string; variables: Record<string, unknown>; headers: Record<string, string>; answer(data: unknown): void }[] = [];
        globalThis.fetch = ((_url: string, init: { body: string; headers: Record<string, string> }) =>
          new Promise((resolve) => {
            const { id, variables } = JSON.parse(init.body);
            requests.push({ id, variables, headers: init.headers, answer: (data) => resolve({ json: async () => ({ data }) } as Response) });
          })) as unknown as typeof fetch;

        const signIn = (key: string) =>
          configureTrax({
            url: "http://host/graphql",
            socketUrl: "ws://host/graphql",
            headers: () => ({ "X-Api-Key": key }),
            connectionParams: () => ({ apiKey: key }),
          });

        const open = (socket: Socket | undefined) => socket?.subscriptions.filter((s) => !s.closed) ?? [];
        const run = (runId: string, fields: Record<string, unknown>) => ({ runId, train: "startRun", state: null, steps: [], failure: null, problem: null, retriedAs: null, ...fields });
        const event = (runId: string, sequence: number, fields: Record<string, unknown>) =>
          ({ data: { runProgress: { runId, sequence, state: null, step: null, failure: null, problem: null, retriedAs: null, ...fields } } });
        const settle = () => act(async () => { await new Promise((r) => setTimeout(r, 0)); });

        afterEach(() => {
          cleanup();
          vi.useRealTimers();
          requests.length = 0;
        });

        it("A_mounted_hook_moves_to_the_new_connection_after_configureTrax", async () => {
          signIn("A");
          renderHook(() => useReminder({ id: "i-1" }));
          await settle();
          const a = sockets.at(-1)!;
          expect(a.params()).toEqual({ apiKey: "A" });
          expect(open(a)).toHaveLength(1);

          await act(async () => signIn("B"));
          await settle();

          const b = sockets.at(-1)!;
          expect(b).not.toBe(a);
          expect(b.params()).toEqual({ apiKey: "B" });
          expect(open(b).map((s) => s.payload.variables.id)).toEqual(["i-1"]);
          expect(requests.at(-1)!.headers["X-Api-Key"]).toBe("B");
        });

        it("A_stream_of_a_server_made_before_configureTrax_is_not_opened_again_on_its_socket", async () => {
          signIn("C");
          vi.useFakeTimers();
          reminderServer().subscribe("i-2", () => {});
          const c = sockets.at(-1)!;
          expect(open(c)).toHaveLength(1);

          signIn("D");
          await vi.advanceTimersByTimeAsync(10_000);

          expect(c.subscriptions).toHaveLength(1);
          expect(sockets.flatMap((s) => s.subscriptions).filter((s) => s.payload.variables.id === "i-2")).toHaveLength(1);
        });

        it("A_read_answered_after_the_stream_ended_does_not_rewind_the_view", async () => {
          signIn("E");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          const stream = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;

          await act(async () => {
            stream.sink.next(event("r1", 1, { state: "COMPLETED" }));
            stream.sink.complete();
          });
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "IN_PROGRESS" }) }));
          await settle();

          expect(result.current.state).toBe("COMPLETED");
          expect(result.current.ended).toBe(true);
        });

        it("A_retry_reported_twice_is_followed_once", async () => {
          signIn("F");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          const stream = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;

          // The run failed and was retried before the page mounted: the read and the stream both say so.
          await act(async () => {
            stream.sink.next(event("r1", 1, { state: "FAILED", retriedAs: "r2" }));
            requests[0].answer({ runProgress: run("r1", { state: "FAILED", retriedAs: "r2" }) });
            await new Promise((r) => setTimeout(r, 0));
          });
          await settle();

          expect(result.current.runId).toBe("r2");
          expect(result.current.earlier).toEqual(["r1"]);
        });

        it("A_read_sent_for_lost_events_is_sent_again_when_more_arrive_before_it_answers", async () => {
          signIn("G");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          const stream = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "PENDING" }) }));

          await act(async () => {
            stream.sink.next(event("r1", 1, { state: "IN_PROGRESS" }));
            stream.sink.next(event("r1", 3, { state: "IN_PROGRESS" }));
          });
          await act(async () => stream.sink.next(event("r1", 4, { state: "IN_PROGRESS" })));
          await act(async () => requests[1].answer({ runProgress: run("r1", { state: "PENDING" }) }));
          await settle();

          expect(requests).toHaveLength(3);
          await act(async () => requests[2].answer({ runProgress: run("r1", { state: "IN_PROGRESS", steps: [{ position: 0, kind: "junction", name: "Lost", state: "COMPLETED" }] }) }));
          await settle();
          expect(result.current.steps.map((s) => s.name)).toEqual(["Lost"]);
        });
        """;

    // An empty project that installs the engine from its npm tarball, and what it needs to typecheck a client.
    private string OutsideProject(string engine, params string[] more)
    {
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
            [
                "install",
                "--no-audit",
                "--no-fund",
                tarball,
                $"typescript@{EngineDevDependency(engine, "typescript")}",
                $"react@{EngineDevDependency(engine, "react")}",
                $"@types/react@{EngineDevDependency(engine, "@types/react")}",
                "graphql-ws@6",
                .. more,
            ]
        );
        installed.Should().Be(0, installOutput);
        return work;
    }

    // The client, generated by the published tool from a manifest as a frontend's build generates it, and checked.
    private static void GenerateClient(string tool, string work)
    {
        File.WriteAllText(
            Path.Combine(work, "manifest.json"),
            Trax.Effect.StateMachine.ClientManifest.Write(Fakes.ClientManifests.Sample())
        );
        string[] client =
        [
            Path.Combine(tool, "trax.dll"),
            "client",
            "--manifest",
            "manifest.json",
            "--out",
            "trax",
        ];
        var (clientCode, clientOutput) = Run("dotnet", work, client);
        clientCode.Should().Be(0, clientOutput);
        var (checkCode, checkOutput) = Run("dotnet", work, [.. client, "--check"]);
        checkCode
            .Should()
            .Be(0, "the client just generated is what the manifest generates: " + checkOutput);
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
