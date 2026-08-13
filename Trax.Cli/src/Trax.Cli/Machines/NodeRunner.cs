using System.Diagnostics;

namespace Trax.Cli.Machines;

/// <summary>The result of running a node script: exit code plus captured stdout/stderr.</summary>
internal sealed record NodeResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs the TypeScript codegen entrypoints (<c>generate-twin.mjs</c> / <c>generate-corpus.mjs</c>) under node.
/// Behind an interface so the generator's orchestration, staging, and drift logic can be unit-tested with a
/// fake that writes canned files, while the real byte-parity tests use the process-spawning implementation.
/// </summary>
internal interface INodeRunner
{
    /// <summary>Whether a working <c>node</c> is on PATH (checked once via <c>node --version</c>).</summary>
    bool IsAvailable();

    /// <summary>Run <c>node &lt;scriptPath&gt; &lt;args...&gt;</c>, capturing output. Never throws on a non-zero exit.</summary>
    NodeResult Run(string scriptPath, IReadOnlyList<string> args);
}

/// <summary>Spawns a real <c>node</c> process. The node binary defaults to <c>node</c> on PATH.</summary>
internal sealed class NodeRunner : INodeRunner
{
    private readonly string _nodePath;

    public NodeRunner(string nodePath = "node") => _nodePath = nodePath;

    public bool IsAvailable()
    {
        try
        {
            var result = Run("--version", []);
            return result.ExitCode == 0;
        }
        catch (Exception)
        {
            // Node not installed / not on PATH: the process fails to start.
            return false;
        }
    }

    public NodeResult Run(string scriptPath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _nodePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start node ('{_nodePath}').");

        // Read both streams fully before waiting, so a child that fills a pipe buffer cannot deadlock.
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new NodeResult(process.ExitCode, stdout, stderr);
    }
}
