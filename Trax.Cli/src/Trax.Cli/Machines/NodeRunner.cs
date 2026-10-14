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
    /// <summary>Whether a working <c>node</c> is found (checked via <c>node --version</c>).</summary>
    bool IsAvailable();

    /// <summary>Run <c>node &lt;scriptPath&gt; &lt;args...&gt;</c>, capturing output. Never throws on a non-zero exit.</summary>
    NodeResult Run(string scriptPath, IReadOnlyList<string> args);
}

/// <summary>
/// Spawns a real <c>node</c> process. The executable is <c>node</c> on <c>PATH</c> by default, or the value of
/// <c>--node</c>; either way it is resolved by <see cref="ExecutableLookup"/>, which never runs a <c>node</c>
/// sitting in the current directory.
/// </summary>
internal sealed class NodeRunner : INodeRunner
{
    private readonly string _command;
    private readonly Func<string, string?> _resolve;
    private string? _resolved;

    public NodeRunner(string command = "node")
        : this(command, ExecutableLookup.Resolve) { }

    internal NodeRunner(string command, Func<string, string?> resolve)
    {
        _command = command;
        _resolve = resolve;
    }

    /// <summary>
    /// Whether the executable is found and answers <c>--version</c>. A relative <c>--node</c> is not "missing"
    /// but refused, so it throws <see cref="InvalidOperationException"/> with the rule instead of returning false.
    /// </summary>
    public bool IsAvailable()
    {
        if (Resolved() is null)
            return false;
        try
        {
            var result = Run("--version", []);
            return result.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Found, but the OS would not start it.
            return false;
        }
    }

    public NodeResult Run(string scriptPath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName =
                Resolved()
                ?? throw new InvalidOperationException(
                    $"'{_command}' was not found on PATH. Install Node.js 22 or later, or pass --node with "
                        + "the absolute path of the node executable."
                ),
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var (exitCode, stdout, stderr) = ChildProcess.Run(psi);
        return new NodeResult(exitCode, stdout, stderr);
    }

    private string? Resolved() => _resolved ??= _resolve(_command);
}
