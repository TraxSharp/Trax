using System.Globalization;

namespace Trax.Cli.Machines;

/// <summary>The outcome of <c>generate</c>: every file placed on disk.</summary>
internal sealed record GenerateResult(IReadOnlyList<GeneratedArtifact> Written);
