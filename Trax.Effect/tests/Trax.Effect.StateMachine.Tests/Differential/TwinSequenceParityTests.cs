using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using Ingest.Contracts;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Differential;

/// <summary>
/// Random sequences of events, outcomes and timers, run through the C# engine here and through the TypeScript
/// twin by <c>Trax.Api.StateMachine/tools/replay-sequences.mjs</c>: the canonical snapshot and the answer agree
/// after every step, and both engines refuse an outcome or a timer trigger sent through <c>advance</c>. Skipped
/// at runtime when node, or the engine's installed dependencies, are absent.
/// </summary>
[Property("adr", UnitTests.TriggerInputValidationTests.Adr)]
public class TwinSequenceParityTests
{
    private const string Adr = UnitTests.TriggerInputValidationTests.Adr;

    // The tool's exit code when the engine's dependencies are not installed.
    private const int MissingDependencies = 3;

    private static readonly TimeSpan NodeTimeout = TimeSpan.FromMinutes(2);

    private enum Op
    {
        Advance,
        Outcome,
        Timer,
    }

    private sealed record Step(Op Op, string Trigger, JsonNode? Input);

    // One machine as both engines see it: the C# entry points, the IR the twin is built from, where a sequence
    // may start, and the pools a step draws its trigger and input from.
    private sealed record Subject(
        string Name,
        JsonObject Ir,
        IReadOnlyList<Snapshot> Starts,
        IReadOnlyList<string> Triggers,
        IReadOnlyList<string> OutcomeTriggers,
        IReadOnlyList<string> TimerTriggers,
        IReadOnlyList<JsonNode?> Inputs,
        Func<Snapshot, Step, AdvanceResult> Apply,
        Func<Snapshot, string> Serialize
    );

    private static Subject For<TState, TTrigger>(
        string name,
        SnapshotMachine<TState, TTrigger> machine,
        JsonObject ir,
        IReadOnlyList<Snapshot> starts,
        IReadOnlyList<string> outcomeTriggers,
        IReadOnlyList<string> timerTriggers,
        IReadOnlyList<JsonNode?> inputs
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum =>
        new(
            name,
            ir,
            starts,
            [.. Enum.GetNames<TTrigger>(), .. outcomeTriggers, .. timerTriggers],
            outcomeTriggers,
            timerTriggers,
            inputs,
            (snapshot, step) =>
                step.Op switch
                {
                    Op.Outcome => machine.AdvanceOutcome(snapshot, step.Trigger, step.Input),
                    Op.Timer => machine.AdvanceTimer(snapshot, step.Trigger),
                    _ => machine.Advance(snapshot, step.Trigger, step.Input),
                },
            machine.Serialize
        );

    // The context is read back from its JSON, as a stored or received snapshot is: a delegate machine may read a
    // number as a double, which a value built from a C# int refuses.
    private static Snapshot Snap(string machine, string state, JsonObject context) =>
        new()
        {
            Machine = machine,
            Version = 1,
            State = state,
            Context = (JsonObject)JsonNode.Parse(context.ToJsonString())!,
        };

    private static JsonObject Export<TState, TTrigger>(BuiltMachine<TState, TTrigger> built)
        where TState : struct, Enum
        where TTrigger : struct, Enum => (JsonObject)JsonNode.Parse(IrExporter.Export(built))!;

    private static IEnumerable<Subject> Subjects(string machinesRoot)
    {
        yield return For(
            "turnstile",
            DeclarativeTurnstile.Machine,
            Export(DeclarativeTurnstile.Built),
            [
                Snap("turnstile", "Locked", new JsonObject()),
                Snap("turnstile", "Unlocked", new JsonObject { ["paidWith"] = "quarter" }),
            ],
            [],
            [],
            [
                null,
                new JsonObject(),
                new JsonObject { ["coin"] = "quarter" },
                new JsonObject { ["coin"] = "penny" },
            ]
        );

        // The twin is built from the committed IR of the declarative checkout, which this C# checkout is authored
        // the same as, so both validate Pay's input against one schema.
        yield return For(
            "checkout",
            DeclarativeCheckout.Machine,
            (JsonObject)
                JsonNode.Parse(
                    File.ReadAllText(Path.Combine(machinesRoot, "checkout", "checkout.ir.json"))
                )!,
            [
                Snap(
                    "checkout",
                    "Cart",
                    new JsonObject
                    {
                        ["currency"] = "USD",
                        ["items"] = new JsonArray(),
                        ["receipt"] = null,
                        ["total"] = 0,
                    }
                ),
                Snap(
                    "checkout",
                    "Review",
                    new JsonObject
                    {
                        ["currency"] = "USD",
                        ["items"] = new JsonArray("latte"),
                        ["receipt"] = null,
                        ["total"] = 5,
                    }
                ),
            ],
            [],
            [],
            [null, new JsonObject(), new JsonObject { ["receipt"] = "r-1" }]
        );

        var output = IngestMachine.Built.Invokes[IngestState.Fetching];
        yield return For(
            "ingest",
            IngestMachine.Machine,
            Export(IngestMachine.Built),
            [
                Snap("ingest", "Idle", new JsonObject { ["source"] = IngestMachine.InitialSource }),
                Snap(
                    "ingest",
                    "Fetching",
                    new JsonObject { ["source"] = IngestMachine.InitialSource }
                ),
            ],
            ["Fetching.done", "Fetching.failed", "Fetching.cancelled"],
            [],
            [
                null,
                output.SerializeOutput(new FetchOutput { Fingerprint = "sha256:abc" }),
                output.SerializeOutput(new FetchOutput { Fingerprint = "sha256:d", Unsure = true }),
                output.SerializeOutput(new FetchOutput { Fingerprint = "" }),
            ]
        );

        yield return For(
            "reminder",
            ReminderMachine.Machine,
            Export(ReminderMachine.Built),
            [ReminderMachine.At(ReminderState.Idle), ReminderMachine.At(ReminderState.Review)],
            [],
            ["Review.after"],
            [null]
        );

        // A timer that reads its duration from the context: the duration grammar is ASCII in both engines, so a
        // duration written with other digits is invalid-input in each.
        var fetched = WaitMachine.Built.Invokes[WaitState.Fetching];
        yield return For(
            "wait",
            WaitMachine.Machine,
            Export(WaitMachine.Built),
            [
                WaitMachine.IdleWith("PT5M"),
                WaitMachine.IdleWith("PT\u0665M"),
                WaitMachine.IdleWith("PT\uFF15M"),
                WaitMachine.IdleWith("P\u0661DT5M"),
                WaitMachine.IdleWith("PT30S"),
            ],
            ["Fetching.done", "Fetching.failed", "Fetching.cancelled"],
            ["Fetching.after"],
            [
                null,
                fetched.SerializeOutput(new FetchOutput { Fingerprint = "sha256:abc" }),
                fetched.SerializeOutput(new FetchOutput { Fingerprint = "" }),
            ]
        );
    }

    private static Gen<(int Start, Step[] Steps)> Sequences(Subject subject) =>
        Gen.Select(
            Gen.Int[0, subject.Starts.Count - 1],
            Gen.Select(Gen.Enum<Op>(), Gen.Int[0, 1_000], Gen.Int[0, 1_000])
                .Select(t => ToStep(subject, t.Item1, t.Item2, t.Item3))
                .Array[0, 30]
        );

    // An op with nothing of its kind to fire falls back to an advance.
    private static Step ToStep(Subject subject, Op op, int pick, int inputPick)
    {
        var input = subject.Inputs[inputPick % subject.Inputs.Count]?.DeepClone();
        return op switch
        {
            Op.Outcome when subject.OutcomeTriggers.Count > 0 => new Step(
                Op.Outcome,
                subject.OutcomeTriggers[pick % subject.OutcomeTriggers.Count],
                input
            ),
            Op.Timer when subject.TimerTriggers.Count > 0 => new Step(
                Op.Timer,
                subject.TimerTriggers[pick % subject.TimerTriggers.Count],
                null
            ),
            _ => new Step(Op.Advance, subject.Triggers[pick % subject.Triggers.Count], input),
        };
    }

    [Test]
    public void Random_event_sequences_with_timers_fired_at_random_points_agree()
    {
        if (!NodeIsAvailable())
            Unavailable("node is not on PATH.");
        var machinesRoot = FixturePaths.MachinesRoot;
        if (machinesRoot is null)
        {
            Unavailable("Trax.Api.StateMachine is not checked out beside this folder.");
            return;
        }
        var engineRoot = Directory.GetParent(machinesRoot)!.FullName;

        // An empty request first: it skips the test when the engine's dependencies are absent, which cannot be
        // done from inside a sample.
        RunTool(
            engineRoot,
            new JsonObject
            {
                ["engineSrc"] = Path.Combine(engineRoot, "src"),
                ["machines"] = new JsonObject(),
                ["sequences"] = new JsonArray(),
            }.ToJsonString()
        );

        foreach (var subject in Subjects(machinesRoot))
            Sequences(subject)
                .Array[500]
                .Sample(batch => AgreeOn(subject, batch, engineRoot), iter: 1);
    }

    private static void AgreeOn(
        Subject subject,
        (int Start, Step[] Steps)[] batch,
        string engineRoot
    )
    {
        var request = new JsonObject
        {
            ["engineSrc"] = Path.Combine(engineRoot, "src"),
            ["machines"] = new JsonObject
            {
                [subject.Name] = new JsonObject { ["ir"] = subject.Ir.DeepClone() },
            },
            ["sequences"] = new JsonArray(
                batch
                    .Select(s =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["machine"] = subject.Name,
                                ["start"] = ToJson(subject.Starts[s.Start]),
                                ["steps"] = new JsonArray(
                                    s.Steps.Select(step =>
                                            (JsonNode)
                                                new JsonObject
                                                {
                                                    ["op"] = step.Op.ToString().ToLowerInvariant(),
                                                    ["trigger"] = step.Trigger,
                                                    ["input"] = step.Input?.DeepClone(),
                                                }
                                        )
                                        .ToArray()
                                ),
                            }
                    )
                    .ToArray()
            ),
        };

        var twin = (JsonArray)JsonNode.Parse(RunTool(engineRoot, request.ToJsonString()))!;

        var diffs = new List<string>();
        for (var i = 0; i < batch.Length; i++)
        {
            var current = subject.Starts[batch[i].Start];
            var answers = (JsonArray)twin[i]!;
            for (var n = 0; n < batch[i].Steps.Length; n++)
            {
                var step = batch[i].Steps[n];
                var result = subject.Apply(current, step);
                if (result is AdvanceResult.Transitioned t)
                    current = t.Snapshot;
                var (outcome, reason) = result switch
                {
                    AdvanceResult.Transitioned => ("transitioned", (string?)null),
                    AdvanceResult.Rejected r => ("rejected", r.Reason.ToWire()),
                    _ => ("unknown", null),
                };
                var wire = subject.Serialize(current);

                // The C# half of the contract: an outcome or a timer is never applied through advance.
                if (
                    step.Op == Op.Advance
                    && (
                        subject.OutcomeTriggers.Contains(step.Trigger)
                        || subject.TimerTriggers.Contains(step.Trigger)
                    )
                    && outcome != "rejected"
                )
                    diffs.Add($"[{subject.Name} #{i} step {n}] C# advance applied {step.Trigger}");

                var answer = (JsonObject)answers[n]!;
                var twinOutcome = answer["outcome"]!.GetValue<string>();
                var twinReason = answer["reason"]?.GetValue<string>();
                var twinWire = answer["wire"]!.GetValue<string>();
                if (outcome != twinOutcome || reason != twinReason || wire != twinWire)
                {
                    diffs.Add(
                        $"[{subject.Name} #{i} step {n}: {step.Op} {step.Trigger} "
                            + $"{step.Input?.ToJsonString()}] C# {outcome} {reason ?? wire}, "
                            + $"twin {twinOutcome} {twinReason ?? twinWire}"
                    );
                    break;
                }
            }
        }

        diffs
            .Should()
            .BeEmpty(
                $"the engines agree after every step ({Adr}):\n" + string.Join("\n", diffs.Take(10))
            );
    }

    private static JsonObject ToJson(Snapshot snapshot) =>
        new()
        {
            ["machine"] = snapshot.Machine,
            ["version"] = snapshot.Version,
            ["state"] = snapshot.State,
            ["context"] = snapshot.Context.DeepClone(),
        };

    private static string RunTool(string engineRoot, string request)
    {
        var start = new ProcessStartInfo("node", "tools/replay-sequences.mjs")
        {
            WorkingDirectory = engineRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(request);
        process.StandardInput.Close();

        // The test owns its wait (ADR 0014): the twin answers well within it, or the test fails.
        if (!process.WaitForExit(NodeTimeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"replay-sequences.mjs did not answer within {NodeTimeout}.");
        }

        if (process.ExitCode == MissingDependencies)
            Unavailable(
                "The engine's dependencies are not installed (npm ci in Trax.Api.StateMachine)."
            );
        process
            .ExitCode.Should()
            .Be(0, $"replay-sequences.mjs failed: {stderr.GetAwaiter().GetResult()}");
        return stdout.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Skips the test on a developer machine without node or the engine's packages, and fails it in CI, which
    /// installs both for this folder (<c>"state-machine": true</c> in <c>.github/ci/packages.json</c>): a skip there
    /// would report green without ever comparing the two engines.
    /// </summary>
    private static void Unavailable(string reason)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            Assert.Fail(
                $"{reason} CI installs it for this test, so the twin parity check cannot be skipped."
            );
        Assert.Ignore($"{reason} Skipping the twin sequence parity test.");
    }

    private static bool NodeIsAvailable()
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo("node", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            );
            if (process is null)
                return false;
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                process.Kill();
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
