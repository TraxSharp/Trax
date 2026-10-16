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

            configureTrax({ url: "/graphql", socketUrl: "ws://localhost/graphql", user: "u-1" });

            export function usePage() {
              const reminder = useReminder({ startInput: {} });
              void reminder.send("Approve");
              // @ts-expect-error a timer trigger is the server's to fire, never a client's send
              void reminder.send("Review.after");
              return reminder.state;
            }

            export function useDraft() {
              // @ts-expect-error the user is the connection's, given to configureTrax, never the hook's
              useRememberedCheckout({ user: "u-2" });
              return useRememberedCheckout({ rememberKey: "drafts" }).forget;
            }

            export function usePay() {
              const checkout = useRememberedCheckout();
              void checkout.send("Pay");
              // @ts-expect-error an effect-bound trigger's input is the receipt the server's effect runner hands it
              void checkout.send("Pay", { receipt: "made-up" });
              return checkout.can("Pay");
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

    // A fresh `npm create vite -- --template react-ts` app turns on flags the engine's own build does not: a parameter
    // property or an enum fails erasableSyntaxOnly, a type imported as a value fails verbatimModuleSyntax, and an unused
    // import fails noUnusedLocals. The generated client is checked under that app's compiler options.
    [Test]
    public void The_generated_client_typechecks_under_a_vite_react_ts_tsconfig()
    {
        var tool = PackAndUnpackTheTool();
        var work = OutsideProject(RepoFolder("Trax.Api.StateMachine"));
        GenerateClient(tool, work);
        File.WriteAllText(
            Path.Combine(work, "main.ts"),
            """
            import { configureTrax, TraxOperationError, useRememberedCheckout, useRunProgress, startRun } from "./trax/index.g";

            configureTrax({ url: "/graphql", socketUrl: "ws://localhost/graphql", user: "u-1" });

            export function usePage(runId: string | null) {
              const checkout = useRememberedCheckout();
              const progress = useRunProgress(runId);
              return { state: checkout.state, steps: progress.steps.length };
            }

            export const failed = (e: unknown) => e instanceof TraxOperationError && e.code === "CLIENT_OUTDATED";
            export const run = () => startRun({ scenario: "ok" });
            """
        );
        // The compiler options create-vite's react-ts template writes into tsconfig.app.json, less its vite/client types.
        File.WriteAllText(
            Path.Combine(work, "tsconfig.json"),
            """
            {
              "compilerOptions": {
                "target": "ES2022",
                "lib": ["ES2023", "DOM", "DOM.Iterable"],
                "module": "ESNext",
                "skipLibCheck": true,
                "moduleResolution": "bundler",
                "allowImportingTsExtensions": true,
                "verbatimModuleSyntax": true,
                "moduleDetection": "force",
                "noEmit": true,
                "jsx": "react-jsx",
                "strict": true,
                "noUnusedLocals": true,
                "noUnusedParameters": true,
                "erasableSyntaxOnly": true,
                "noFallthroughCasesInSwitch": true,
                "noUncheckedSideEffectImports": true
              },
              "include": ["main.ts", "trax/**/*.ts"]
            }
            """
        );

        var (code, output) = Run(
            "node",
            work,
            Path.Combine(work, "node_modules", "typescript", "bin", "tsc"),
            "-p",
            "tsconfig.json"
        );

        code.Should().Be(0, output);
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

        import { configureTrax, followRunProgress, reminderServer, TraxOperationError, useRememberedReminder, useReminder, useRunProgress } from "./trax/index.g";

        // Every request, with its headers, answered only when the test says so.
        const requests: { id: string; variables: Record<string, unknown>; headers: Record<string, string>; answer(data: unknown): void }[] = [];
        globalThis.fetch = ((_url: string, init: { body: string; headers: Record<string, string> }) =>
          new Promise((resolve) => {
            const { id, variables } = JSON.parse(init.body);
            requests.push({ id, variables, headers: init.headers, answer: (data) => resolve({ json: async () => ({ data }) } as Response) });
          })) as unknown as typeof fetch;

        const signIn = (key: string, user = key) =>
          configureTrax({
            url: "http://host/graphql",
            socketUrl: "ws://host/graphql",
            headers: () => ({ "X-Api-Key": key }),
            connectionParams: () => ({ apiKey: key }),
            user,
          });

        // The reminder's starts and reads as the host answers them: its initial state is Idle, with an empty context.
        const starts = () => requests.filter((r) => r.id.startsWith("trax_machine_reminder_start_"));
        const reads = (id: string) => requests.filter((r) => r.id.startsWith("trax_machine_reminder_get_") && r.variables.id === id);
        const instance = (id: string) => ({ id, state: "Idle", context: {}, revision: 1, problem: null });
        const started = (id: string) => ({ machines: { reminder: { start: instance(id) } } });
        const got = (id: string) => ({ machines: { reminder: { get: instance(id) } } });

        const open = (socket: Socket | undefined) => socket?.subscriptions.filter((s) => !s.closed) ?? [];
        const run = (runId: string, fields: Record<string, unknown>) => ({ runId, train: "startRun", state: null, steps: [], failure: null, problem: null, retriedAs: null, ...fields });
        const event = (runId: string, sequence: number, fields: Record<string, unknown>) =>
          ({ data: { runProgress: { runId, sequence, state: null, step: null, failure: null, problem: null, retriedAs: null, ...fields } } });
        const settle = () => act(async () => { await new Promise((r) => setTimeout(r, 0)); });

        afterEach(() => {
          cleanup();
          vi.useRealTimers();
          requests.length = 0;
          localStorage.clear();
          sessionStorage.clear();
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

        it("A_server_made_before_configureTrax_refuses_to_send", async () => {
          signIn("J");
          const server = reminderServer();
          signIn("K");

          await expect(server.get("i-4")).rejects.toMatchObject({ name: "TraxOperationError", code: "CONNECTION_CHANGED" });
          expect(requests).toHaveLength(0);
          // The stream ends for good with the refusal: no timer is left to subscribe it again.
          vi.useFakeTimers();
          const messages: unknown[] = [];
          const stop = server.subscribe("i-4", (m) => messages.push(m));
          await vi.advanceTimersByTimeAsync(10_000);
          expect(messages).toEqual([expect.objectContaining({ type: "ended", code: "CONNECTION_CHANGED" })]);
          expect(vi.getTimerCount()).toBe(0);
          stop();
          expect(sockets.flatMap((s) => s.subscriptions).filter((s) => s.payload.variables.id === "i-4")).toHaveLength(0);
          expect(reminderServer()).not.toBe(server);
        });

        it("The_remembered_key_follows_the_user_given_to_configureTrax", async () => {
          signIn("L", "alice");
          const { result } = renderHook(() => useRememberedReminder());
          await settle();
          const aliceStart = starts().at(-1)!;
          expect(aliceStart.headers["X-Api-Key"]).toBe("L");
          await act(async () => aliceStart.answer(started("rem-a")));
          await settle();
          expect(localStorage.getItem("trax.reminder.alice")).toBe("rem-a");

          await act(async () => signIn("M", "bob"));
          await settle();

          const bobStart = starts().at(-1)!;
          expect(bobStart.headers["X-Api-Key"]).toBe("M");
          expect(bobStart.variables.requestId).not.toBe(aliceStart.variables.requestId);
          await act(async () => bobStart.answer(started("rem-b")));
          await settle();
          expect(result.current.id).toBe("rem-b");
          expect(localStorage.getItem("trax.reminder.bob")).toBe("rem-b");
          expect(localStorage.getItem("trax.reminder.alice")).toBe("rem-a");
        });

        it("A_connection_change_before_the_user_changes_writes_nothing_under_the_previous_user", async () => {
          signIn("N", "alice");
          // The app's own idea of who is signed in moves a render after configureTrax does.
          const { rerender } = renderHook(({ user }: { user: string }) => ({ user, m: useRememberedReminder() }), {
            initialProps: { user: "alice" },
          });
          await settle();
          const aliceStart = starts().at(-1)!;

          await act(async () => signIn("O", "bob"));
          await settle();
          await act(async () => aliceStart.answer(started("rem-a2")));
          await settle();
          rerender({ user: "bob" });
          await settle();

          const bobStarts = starts().filter((r) => r.headers["X-Api-Key"] === "O");
          expect(bobStarts.length).toBeGreaterThan(0);
          expect(bobStarts.map((r) => r.variables.requestId)).not.toContain(aliceStart.variables.requestId);
          expect(localStorage.getItem("trax.reminder.alice")).toBeNull();
          expect(localStorage.getItem("trax.reminder.bob")).toBeNull();
          await act(async () => bobStarts.at(-1)!.answer(started("rem-b2")));
          await settle();
          expect(localStorage.getItem("trax.reminder.bob")).toBe("rem-b2");
          expect(localStorage.getItem("trax.reminder.alice")).toBeNull();
        });

        it("The_first_render_after_configureTrax_shows_nothing_of_the_previous_user", async () => {
          signIn("P", "alice");
          let phase = "alice";
          const seen: { phase: string; state: string | null }[] = [];
          renderHook(() => {
            const m = useReminder({ id: "i-5" });
            seen.push({ phase, state: m.state });
            return m;
          });
          await settle();
          await act(async () => reads("i-5")[0].answer(got("i-5")));
          await settle();
          expect(seen.at(-1)).toEqual({ phase: "alice", state: "Idle" });

          phase = "bob";
          await act(async () => signIn("Q", "bob"));
          await settle();

          const bob = seen.filter((s) => s.phase === "bob");
          expect(bob.length).toBeGreaterThan(0);
          expect(bob.map((s) => s.state)).toEqual(bob.map(() => null));
        });

        it("A_start_request_id_is_not_carried_across_configureTrax", async () => {
          signIn("R", "alice");
          renderHook(() => useReminder());
          await settle();
          const aliceStart = starts().at(-1)!;

          await act(async () => signIn("S", "bob"));
          await settle();

          const bobStart = starts().at(-1)!;
          expect(bobStart).not.toBe(aliceStart);
          expect(bobStart.headers["X-Api-Key"]).toBe("S");
          expect(bobStart.variables.requestId).not.toBe(aliceStart.variables.requestId);
          expect(sessionStorage.getItem("trax-start:reminder.alice")).toBe(aliceStart.variables.requestId);
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

        it("A_read_answered_after_the_first_event_still_shows_the_earlier_steps", async () => {
          signIn("E2");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          const stream = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;

          // On a warm socket the state-only first event beats the read, which holds the steps taken before mount.
          await act(async () => stream.sink.next(event("r1", 1, { state: "IN_PROGRESS" })));
          const steps = [
            { position: 0, kind: "junction", name: "Load", state: "COMPLETED" },
            { position: 1, kind: "junction", name: "Charge", state: "IN_PROGRESS" },
          ];
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "PENDING", steps }) }));
          await settle();
          expect(result.current.state).toBe("IN_PROGRESS");
          expect(result.current.steps.map((s) => s.name)).toEqual(["Load", "Charge"]);

          // A step an event reported is newer than the read's.
          await act(async () => stream.sink.next(event("r1", 2, { step: { ...steps[1], state: "COMPLETED" } })));
          expect(result.current.steps.map((s) => s.state)).toEqual(["COMPLETED", "COMPLETED"]);
        });

        it("A_followed_run_is_followed_again_after_configureTrax", async () => {
          signIn("H");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          const h = sockets.at(-1)!;
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "IN_PROGRESS" }) }));
          expect(open(h).filter((s) => s.payload.variables.runId === "r1")).toHaveLength(1);

          // The token is refreshed for the same user.
          await act(async () => signIn("I", "H"));
          await settle();
          await act(async () => requests.at(-1)!.answer({ runProgress: run("r1", { state: "IN_PROGRESS" }) }));

          expect(result.current.ended).toBe(false);
          expect(result.current.state).toBe("IN_PROGRESS");
          const i = sockets.at(-1)!;
          expect(i).not.toBe(h);
          expect(i.params()).toEqual({ apiKey: "I" });
          expect(open(i).filter((s) => s.payload.variables.runId === "r1")).toHaveLength(1);
          expect(requests.at(-1)!.headers["X-Api-Key"]).toBe("I");
        });

        it("A_run_refused_before_configureTrax_is_live_again_after_it", async () => {
          signIn("J");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "IN_PROGRESS" }) }));
          const j = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;

          // The token expired: the host refuses the stream, and the run shows as ended.
          await act(async () => j.sink.next({ errors: [{ message: "expired", extensions: { code: "AUTH_NOT_AUTHENTICATED" } }] }));
          expect(result.current.ended).toBe(true);

          await act(async () => signIn("K", "J"));
          await settle();
          expect(result.current.ended).toBe(false);
          const k = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;
          await act(async () => k.sink.next(event("r1", 1, { state: "COMPLETED" })));

          expect(result.current.state).toBe("COMPLETED");
          expect(result.current.ended).toBe(false);
        });

        it("The_first_render_after_configureTrax_shows_nothing_of_the_previous_users_run", async () => {
          signIn("L");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          const steps = [{ position: 0, kind: "junction", name: "Load", state: "COMPLETED" }];
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "FAILED", steps, failure: "L's failure" }) }));
          expect(result.current.steps).toHaveLength(1);

          await act(async () => signIn("M"));

          expect(result.current.runId).toBeNull();
          expect(result.current.state).toBeNull();
          expect(result.current.steps).toEqual([]);
          expect(result.current.failure).toBeNull();
          expect(result.current.problem).toBeNull();
          expect(result.current.ended).toBe(false);
        });

        it("A_run_id_given_under_the_previous_user_is_not_followed_after_the_user_changes", async () => {
          signIn("N");
          const { result, rerender } = renderHook(({ id }) => useRunProgress(id), { initialProps: { id: "r1" as string | null } });
          await settle();
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "IN_PROGRESS" }) }));
          const sent = requests.length;

          await act(async () => signIn("O"));
          await settle();

          expect(requests).toHaveLength(sent);
          expect(open(sockets.at(-1)).filter((s) => s.payload.variables.runId === "r1")).toHaveLength(0);
          expect(result.current.runId).toBeNull();
          expect(result.current.state).toBeNull();

          // The caller gives the run id again: it is the new user's to follow.
          rerender({ id: null });
          rerender({ id: "r1" });
          await settle();
          expect(requests.at(-1)!.headers["X-Api-Key"]).toBe("O");
          expect(open(sockets.at(-1)).filter((s) => s.payload.variables.runId === "r1")).toHaveLength(1);
          expect(result.current.runId).toBe("r1");
        });

        it("A_hook_mounted_mid_run_shows_a_step_its_read_and_its_stream_both_missed", async () => {
          signIn("Q");
          const { result } = renderHook(() => useRunProgress("r1"));
          await settle();
          const stream = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;
          const step = (position: number, name: string, state = "COMPLETED") => ({ position, kind: "junction", name, state });

          // Step 2 was pushed before the stream opened and written after both reads were answered.
          await act(async () => stream.sink.next(event("r1", 1, { state: "IN_PROGRESS" })));
          expect(requests).toHaveLength(2);
          await act(async () => requests[0].answer({ runProgress: run("r1", { state: "IN_PROGRESS", steps: [step(0, "Load"), step(1, "Check")] }) }));
          await act(async () => requests[1].answer({ runProgress: run("r1", { state: "IN_PROGRESS", steps: [step(0, "Load"), step(1, "Check")] }) }));
          await act(async () => {
            stream.sink.next(event("r1", 2, { step: step(3, "Pay", "IN_PROGRESS") }));
            stream.sink.next(event("r1", 3, { step: step(3, "Pay") }));
            stream.sink.next(event("r1", 4, { state: "COMPLETED" }));
            stream.sink.complete();
          });
          expect(result.current.steps.map((s) => s.name)).toEqual(["Load", "Check", "Pay"]);

          expect(requests).toHaveLength(3);
          await act(async () =>
            requests[2].answer({ runProgress: run("r1", { state: "COMPLETED", steps: [step(0, "Load"), step(1, "Check"), step(2, "Route"), step(3, "Pay", "IN_PROGRESS")] }) }),
          );
          await settle();

          expect(result.current.steps.map((s) => `${s.name}:${s.state}`)).toEqual(["Load:COMPLETED", "Check:COMPLETED", "Route:COMPLETED", "Pay:COMPLETED"]);
          expect(result.current.state).toBe("COMPLETED");
          expect(result.current.ended).toBe(true);
        });

        it("followRunProgress_ends_with_CONNECTION_CHANGED_when_configureTrax_closes_its_socket", async () => {
          signIn("S");
          const ends: unknown[] = [];
          const events: unknown[] = [];
          followRunProgress("r1", (e) => events.push(e), (error) => ends.push(error));
          const stream = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;
          stream.sink.next(event("r1", 1, { state: "IN_PROGRESS" }));

          // A token refresh for the same user: the run has not ended.
          signIn("S2", "S");

          expect(events).toHaveLength(1);
          expect(ends).toHaveLength(1);
          expect(ends[0]).toBeInstanceOf(TraxOperationError);
          expect((ends[0] as TraxOperationError).code).toBe("CONNECTION_CHANGED");
        });

        it("followRunProgress_ends_with_no_error_when_the_run_ends", async () => {
          signIn("T");
          const ends: unknown[] = [];
          followRunProgress("r1", () => undefined, (error) => ends.push(error));
          const stream = open(sockets.at(-1)).find((s) => s.payload.variables.runId === "r1")!;

          stream.sink.next(event("r1", 1, { state: "COMPLETED" }));
          stream.sink.complete();

          expect(ends).toEqual([undefined]);
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
          // requests[1] is the read sent once the stream opened; requests[2] the one sent for the lost event.
          await act(async () => requests[2].answer({ runProgress: run("r1", { state: "PENDING" }) }));
          await settle();

          expect(requests).toHaveLength(4);
          await act(async () => requests[3].answer({ runProgress: run("r1", { state: "IN_PROGRESS", steps: [{ position: 0, kind: "junction", name: "Lost", state: "COMPLETED" }] }) }));
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
        // The page's own document of the train, from which its function is generated.
        Directory.CreateDirectory(Path.Combine(work, "documents"));
        File.WriteAllText(
            Path.Combine(work, "documents", "startRun.graphql"),
            Fakes.ClientManifests.StartRunDocument
        );
        string[] client =
        [
            Path.Combine(tool, "trax.dll"),
            "client",
            "--manifest",
            "manifest.json",
            "--out",
            "trax",
            "--documents",
            "documents/*.graphql",
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
