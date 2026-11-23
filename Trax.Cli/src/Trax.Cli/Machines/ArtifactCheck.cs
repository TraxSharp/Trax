using System.Globalization;

namespace Trax.Cli.Machines;

/// <summary>The drift verdict for one artifact under <c>check</c>.</summary>
internal sealed record ArtifactCheck(string Kind, string Path, DriftStatus Status);
