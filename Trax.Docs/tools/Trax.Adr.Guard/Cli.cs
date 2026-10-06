namespace Trax.Adr.Guard;

/// <summary>Argument parsing for the console entry point.</summary>
public static class Cli
{
    public const string Usage = """
        Usage: trax-adr-guard [options]

          --repo <path>             Repository to check. Default: the current directory.
          --adr-root <path>         ADR directory, repo-relative. Default: docs/adr
          --test-roots <a,b>        Folders scanned for exemplar classes. Default: tests
          --known-areas <a,b>       Closed vocabulary for the 'areas' key. Required.
          --known-repos <a,b>       Closed vocabulary for the 'repos' key. Central corpus only.
          --require-repos-key       Require 'repos' (the central corpus) instead of forbidding it.
          --census-root <path>      Also require every guard class there to be classified.
        """;

    public sealed record Parsed(GuardOptions? Options, string? Error);

    public static Parsed Parse(string[] args)
    {
        var repo = Directory.GetCurrentDirectory();
        var adrRoot = "docs/adr";
        var testRoots = new List<string> { "tests" };
        var areas = new HashSet<string>(StringComparer.Ordinal);
        var repos = new HashSet<string>(StringComparer.Ordinal);
        var requireRepos = false;
        string? censusRoot = null;

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];

            if (flag == "--require-repos-key")
            {
                requireRepos = true;
                continue;
            }

            if (i + 1 >= args.Length)
                return new Parsed(null, $"'{flag}' needs a value.");

            var value = args[++i];
            switch (flag)
            {
                case "--repo":
                    repo = value;
                    break;
                case "--adr-root":
                    adrRoot = value.Replace('\\', '/').Trim('/');
                    break;
                case "--test-roots":
                    testRoots = Split(value);
                    break;
                case "--known-areas":
                    areas = [.. Split(value)];
                    break;
                case "--known-repos":
                    repos = [.. Split(value)];
                    break;
                case "--census-root":
                    censusRoot = value.Replace('\\', '/').Trim('/');
                    break;
                default:
                    return new Parsed(null, $"Unknown option '{flag}'.");
            }
        }

        if (!Directory.Exists(repo))
            return new Parsed(null, $"Repository '{repo}' does not exist.");

        if (areas.Count == 0)
            return new Parsed(
                null,
                "--known-areas is required. The area vocabulary is closed on purpose: one that "
                    + "grows freely stops discriminating."
            );

        if (requireRepos && repos.Count == 0)
            return new Parsed(null, "--require-repos-key needs --known-repos to validate against.");

        return new Parsed(
            new GuardOptions
            {
                RepoRoot = Path.GetFullPath(repo),
                AdrRoot = adrRoot,
                TestRoots = testRoots,
                KnownAreas = areas,
                KnownRepos = repos,
                RequireReposKey = requireRepos,
                CensusRoot = censusRoot,
            },
            null
        );
    }

    private static List<string> Split(string value) =>
        value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
