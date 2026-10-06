namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Every sample keeps its runs in a database no other sample uses, and docker-compose creates
/// it. A scheduler polls every manifest and job in its database whatever train it names: two
/// samples on one database queue each other's manifests, and a host that does not know a train
/// fails its runs until the manifest dead-letters. The processes of one sample (a hub and its
/// workers, an API and its runner) do share one, which is why the unit is the sample folder.
///
/// <para>Not ADR-enforcing: it keeps the sample hosts from colliding on one database, which is
/// a property of the scheduler rather than a choice between alternatives.</para>
/// </summary>
[TestFixture]
public class SampleDatabasesAreSeparateTests
{
    private static readonly Regex Database = new(@"Database=(?<db>[^;""]+)", RegexOptions.Compiled);

    /// <summary>Every database each sample folder names, keyed by database.</summary>
    private static Dictionary<string, HashSet<string>> FoldersByDatabase()
    {
        var samples = Path.Combine(RepoRoot.Path, "samples");
        var files = Directory
            .EnumerateFiles(samples, "appsettings*.json", SearchOption.AllDirectories)
            .Concat(SourceFiles.CSharp("samples"))
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            );

        var byDatabase = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var folder = Path.GetRelativePath(samples, file).Split(Path.DirectorySeparatorChar)[0];
            foreach (Match match in Database.Matches(File.ReadAllText(file)))
            {
                var db = match.Groups["db"].Value;
                if (!byDatabase.TryGetValue(db, out var folders))
                    byDatabase[db] = folders = new HashSet<string>(StringComparer.Ordinal);
                folders.Add(folder);
            }
        }
        return byDatabase;
    }

    [Test]
    public void No_two_samples_share_a_database()
    {
        var byDatabase = FoldersByDatabase();
        byDatabase.Should().NotBeEmpty("the Postgres samples name their databases");

        var shared = byDatabase
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.OrderBy(f => f))}")
            .ToList();

        shared
            .Should()
            .BeEmpty(
                "a scheduler runs every manifest in its database, so two samples on one database "
                    + "fail each other's runs. Shared:\n  "
                    + string.Join("\n  ", shared)
            );
    }

    [Test]
    public void Docker_compose_creates_every_sample_database()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot.Path, "docker-compose.yml"));
        var created = Regex
            .Match(compose, @"POSTGRES_MULTIPLE_DATABASES:\s*(?<dbs>\S+)")
            .Groups["dbs"]
            .Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Append(Regex.Match(compose, @"POSTGRES_DB:\s*(?<db>\S+)").Groups["db"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = FoldersByDatabase().Keys.Where(db => !created.Contains(db)).ToList();

        missing
            .Should()
            .BeEmpty(
                "`docker compose up -d` must create every database a sample connects to. Missing "
                    + "from POSTGRES_MULTIPLE_DATABASES: "
                    + string.Join(", ", missing)
            );
    }
}
