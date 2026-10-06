namespace Trax.Core.Testing;

/// <summary>
/// Configuration for the architecture-guard checkers. Defaults match the Trax conventions; a consumer
/// overrides only what differs (scan roots, allowlists, expected versions). Allowlist paths are
/// repo-relative and use forward slashes.
/// </summary>
public sealed record ArchitectureGuardOptions
{
    /// <summary>
    /// Overrides the repository root the guards scan. Defaults to the auto-detected root (walk up to a
    /// <c>*.slnx</c>). Set this only to point guards at a specific tree (primarily for testing the
    /// guards themselves against a synthetic fixture directory).
    /// </summary>
    public string? RepoRootOverride { get; init; }

    /// <summary>Top-level folders containing test code (scanned by the test-hygiene guards).</summary>
    public IReadOnlyList<string> TestScanRoots { get; init; } = ["tests"];

    /// <summary>
    /// Top-level folders containing production source (scanned by the data-layer / GraphQL / train
    /// convention guards). Defaults to <c>src</c>; a consumer overrides (e.g. <c>["samples", "lib"]</c>).
    /// </summary>
    public IReadOnlyList<string> SourceScanRoots { get; init; } = ["src"];

    /// <summary>Files exempt from the no-<c>[Ignore]</c> guard (each should carry a justification in source).</summary>
    public IReadOnlySet<string> NoIgnoreKnownExceptions { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Files exempt from the no-fixed-delay guard.</summary>
    public IReadOnlySet<string> FixedDelayKnownExceptions { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The exact <c>&lt;Version&gt;</c> the root <c>Directory.Build.props</c> must declare for local dev.</summary>
    public string ExpectedDirectoryBuildPropsVersion { get; init; } = "1.99.99";

    /// <summary>Package-name prefix treated as a cross-repo Trax dependency.</summary>
    public string TraxPackagePrefix { get; init; } = "Trax.";

    /// <summary>Project files exempt from the cross-repo package-version guard.</summary>
    public IReadOnlySet<string> CrossRepoPackageKnownExceptions { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);
}
