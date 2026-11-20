using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Cli.Machines;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.Fakes;

/// <summary>
/// A stand-in for the real <see cref="NodeRunner"/> so the generator's orchestration, staging, placement, and
/// drift logic can be tested without spawning node. By default it emulates the entrypoints: a <c>twin</c> call
/// writes canned <c>&lt;id&gt;.contexts.g.ts</c> / <c>&lt;id&gt;.machine.g.ts</c> into <c>--out-dir</c>, a
/// <c>corpus</c> call writes canned content to <c>--out</c>. Toggle <see cref="Available"/> /
/// <see cref="ExitCode"/> to drive the error paths.
/// </summary>
internal sealed class FakeNodeRunner : INodeRunner
{
    public bool Available { get; set; } = true;
    public int ExitCode { get; set; }
    public string StdErr { get; set; } = "";
    public string ContextsContent { get; set; } = "// generated contexts\n";
    public string MachineContent { get; set; } = "// generated machine\n";
    public string CorpusContent { get; set; } = "{ \"cases\": [] }\n";
    public string ViewContent { get; set; } = "// generated view\n";

    /// <summary>When false, every call exits 0 without writing anything, as a mismatched engine might.</summary>
    public bool WritesOutputs { get; set; } = true;
    public List<(string Script, IReadOnlyList<string> Args)> Calls { get; } = [];

    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);

    public async Task<NodeResult> RunAsync(string scriptPath, IReadOnlyList<string> args)
    {
        Calls.Add((scriptPath, args));
        if (ExitCode != 0)
            return new NodeResult(ExitCode, "", StdErr);

        if (!WritesOutputs)
            return new NodeResult(0, "", "");

        var flags = ParseFlags(args);
        var script = Path.GetFileName(scriptPath);
        if (script.Contains("twin", StringComparison.Ordinal))
        {
            var outDir = flags["--out-dir"];
            var id = await ReadIrIdAsync(flags["--ir"]).ConfigureAwait(false);
            if (
                string.Equals(flags.GetValueOrDefault("--lang"), "csharp", StringComparison.Ordinal)
            )
            {
                await File.WriteAllTextAsync(
                        Path.Combine(outDir, $"{MachineScaffolder.Pascal(id)}.g.cs"),
                        ViewContent
                    )
                    .ConfigureAwait(false);
                return new NodeResult(0, "", "");
            }
            await File.WriteAllTextAsync(
                    Path.Combine(outDir, $"{id}.contexts.g.ts"),
                    ContextsContent
                )
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(outDir, $"{id}.machine.g.ts"), MachineContent)
                .ConfigureAwait(false);
        }
        else if (script.Contains("corpus", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(flags["--out"], CorpusContent).ConfigureAwait(false);
        }

        return new NodeResult(0, "", "");
    }

    private static async Task<string> ReadIrIdAsync(string irPath) =>
        (
            (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(irPath).ConfigureAwait(false)).NotNull()
        )["id"]
            .NotNull()
            .GetValue<string>();

    private static Dictionary<string, string> ParseFlags(IReadOnlyList<string> args)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                flags[args[i]] = args[i + 1];
        return flags;
    }
}
