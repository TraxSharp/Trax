using System.Diagnostics;

namespace Trax.Cli.Machines;

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
