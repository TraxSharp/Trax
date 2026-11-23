using System.Diagnostics;

namespace Trax.Cli.Machines;

/// <summary>The result of running a node script: exit code plus captured stdout/stderr.</summary>
internal sealed record NodeResult(int ExitCode, string StdOut, string StdErr);
