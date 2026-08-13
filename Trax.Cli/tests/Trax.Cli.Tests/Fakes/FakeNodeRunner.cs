using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Cli.Machines;

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
    public List<(string Script, IReadOnlyList<string> Args)> Calls { get; } = [];

    public bool IsAvailable() => Available;

    public NodeResult Run(string scriptPath, IReadOnlyList<string> args)
    {
        Calls.Add((scriptPath, args));
        if (ExitCode != 0)
            return new NodeResult(ExitCode, "", StdErr);

        var flags = ParseFlags(args);
        var script = Path.GetFileName(scriptPath);
        if (script.Contains("twin", StringComparison.Ordinal))
        {
            var outDir = flags["--out-dir"];
            var id = ReadIrId(flags["--ir"]);
            File.WriteAllText(Path.Combine(outDir, $"{id}.contexts.g.ts"), ContextsContent);
            File.WriteAllText(Path.Combine(outDir, $"{id}.machine.g.ts"), MachineContent);
        }
        else if (script.Contains("corpus", StringComparison.Ordinal))
        {
            File.WriteAllText(flags["--out"], CorpusContent);
        }

        return new NodeResult(0, "", "");
    }

    private static string ReadIrId(string irPath) =>
        ((JsonObject)JsonNode.Parse(File.ReadAllText(irPath))!)["id"]!.GetValue<string>();

    private static Dictionary<string, string> ParseFlags(IReadOnlyList<string> args)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                flags[args[i]] = args[i + 1];
        return flags;
    }
}
