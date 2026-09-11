namespace Trax.Adr.Guard;

/// <summary>
/// Configuration for the ADR checks. Defaults match the Trax conventions; a caller
/// overrides only what differs. Paths are repo-relative and use forward slashes.
/// </summary>
/// <remarks>
/// Mirrors <c>ArchitectureGuardOptions</c> in <c>Trax.Core.Testing</c>, which is the
/// established shape for guard configuration in this workspace. It is deliberately not
/// shared with that package: <c>Trax.Core.Testing</c> ships to nuget.org for consumers of
/// the framework, and ADR linting is a project-internal concern with no place in it.
/// </remarks>
public sealed record GuardOptions
{
    /// <summary>The repository being checked. Every other path is relative to this.</summary>
    public required string RepoRoot { get; init; }

    /// <summary>Directory holding the ADRs, relative to <see cref="RepoRoot"/>.</summary>
    public string AdrRoot { get; init; } = "docs/adr";

    /// <summary>Top-level folders scanned for exemplar guard classes.</summary>
    public IReadOnlyList<string> TestRoots { get; init; } = ["tests"];

    /// <summary>
    /// The closed vocabulary an ADR's <c>areas</c> must draw from. Hand-maintained on
    /// purpose: a vocabulary that grows freely stops discriminating.
    /// </summary>
    public IReadOnlySet<string> KnownAreas { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The closed vocabulary a central ADR's <c>repos</c> must draw from. Empty for a
    /// repo-local corpus, where the key is forbidden.
    /// </summary>
    public IReadOnlySet<string> KnownRepos { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// True for the central corpus in Trax.Docs, where <c>repos</c> says which repos must
    /// obey. False for a repo-local corpus, where the path already says it and the key is
    /// rejected as a second place to drift.
    /// </summary>
    public bool RequireReposKey { get; init; }

    /// <summary>
    /// Folder whose guard classes must each be credited to an ADR or opt out with a
    /// reason. Null disables the census.
    /// </summary>
    public string? CensusRoot { get; init; }
}
