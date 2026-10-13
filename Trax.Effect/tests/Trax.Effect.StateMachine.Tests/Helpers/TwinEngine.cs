using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using AwesomeAssertions;

namespace Trax.Effect.StateMachine.Tests.Helpers;

/// <summary>
/// The TypeScript twin, run from a C# test: <c>Trax.Api.StateMachine/tools/replay-sequences.mjs</c> bundles the
/// engine's source with esbuild and replays one-step-or-longer sequences against a machine built from its IR,
/// answering each step's outcome, reason and canonical wire. When node, the engine's source or its installed
/// dependencies are missing, the test is skipped on a developer's machine and fails under CI (the <c>CI</c>
/// variable set), where a skipped parity test would prove nothing.
/// </summary>
public static class TwinEngine
{
    // The tool's exit code when the engine's dependencies are not installed.
    private const int MissingDependencies = 3;

    private static readonly TimeSpan NodeTimeout = TimeSpan.FromMinutes(2);

    /// <summary>One step of a sequence: <c>advance</c>, <c>outcome</c> or <c>timer</c>.</summary>
    public sealed record Step(string Op, string Trigger, JsonNode? Input);

    /// <summary>A sequence of steps from <paramref name="Start"/> on the machine named <paramref name="Machine"/>.</summary>
    public sealed record Sequence(string Machine, Snapshot Start, IReadOnlyList<Step> Steps);

    /// <summary>What the twin answered to one step.</summary>
    public sealed record Answer(string Outcome, string? Reason, string Wire);

    /// <summary>
    /// Replays <paramref name="sequences"/> through the twin, each machine built from its IR in
    /// <paramref name="irs"/>, and returns its answers, one list per sequence and one answer per step.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Answer>> Replay(
        IReadOnlyDictionary<string, JsonObject> irs,
        IReadOnlyList<Sequence> sequences
    )
    {
        var engineRoot = EngineRoot();
        var machines = new JsonObject();
        foreach (var (name, ir) in irs)
            machines[name] = new JsonObject { ["ir"] = ir.DeepClone() };
        var request = new JsonObject
        {
            ["engineSrc"] = Path.Combine(engineRoot, "src"),
            ["machines"] = machines,
            ["sequences"] = new JsonArray(
                sequences
                    .Select(s =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["machine"] = s.Machine,
                                ["start"] = new JsonObject
                                {
                                    ["machine"] = s.Start.Machine,
                                    ["version"] = s.Start.Version,
                                    ["state"] = s.Start.State,
                                    ["context"] = s.Start.Context.DeepClone(),
                                },
                                ["steps"] = new JsonArray(
                                    s.Steps.Select(step =>
                                            (JsonNode)
                                                new JsonObject
                                                {
                                                    ["op"] = step.Op,
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

        var answers = (JsonArray)JsonNode.Parse(Run(engineRoot, request.ToJsonString()))!;
        return answers
            .Select(sequence =>
                (IReadOnlyList<Answer>)
                    ((JsonArray)sequence!)
                        .Select(a => new Answer(
                            a!["outcome"]!.GetValue<string>(),
                            a["reason"]?.GetValue<string>(),
                            a["wire"]!.GetValue<string>()
                        ))
                        .ToList()
            )
            .ToList();
    }

    private static string EngineRoot()
    {
        if (!NodeIsAvailable())
            Unavailable("node is not on PATH.");
        if (FixturePaths.MachinesRoot is not { } machinesRoot)
        {
            Unavailable("Trax.Api.StateMachine is not checked out beside this folder.");
            throw new UnreachableException();
        }
        return Directory.GetParent(machinesRoot)!.FullName;
    }

    // Skips the test on a developer's machine; fails it under CI, where the engine is always installed.
    private static void Unavailable(string why)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            Assert.Fail(
                $"The TypeScript twin cannot run: {why} CI installs it (npm ci in Trax.Api.StateMachine), so a "
                    + "parity test that skips there proves nothing."
            );
        Assert.Ignore($"The TypeScript twin cannot run: {why} Skipping the cross-engine check.");
    }

    private static string Run(string engineRoot, string request)
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
            Unavailable("its dependencies are not installed (npm ci in Trax.Api.StateMachine).");
        process
            .ExitCode.Should()
            .Be(0, $"replay-sequences.mjs failed: {stderr.GetAwaiter().GetResult()}");
        return stdout.GetAwaiter().GetResult();
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
