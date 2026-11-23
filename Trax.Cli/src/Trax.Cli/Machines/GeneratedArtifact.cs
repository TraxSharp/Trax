using System.Globalization;

namespace Trax.Cli.Machines;

/// <summary>An artifact written by <c>generate</c>: its kind and final path.</summary>
internal sealed record GeneratedArtifact(string Kind, string Path);
