using System.Globalization;

namespace Trax.Cli.Machines;

/// <summary>The outcome of <c>check</c>: per-artifact drift; clean iff every artifact is up to date.</summary>
internal sealed record CheckResult(IReadOnlyList<ArtifactCheck> Checks)
{
    public bool IsClean => Checks.All(c => c.Status == DriftStatus.UpToDate);
}
