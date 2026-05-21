namespace Trax.Effect.Tests.Meta.Tests;

[TestFixture]
public class MigrationsIntegrityTests
{
    public static IEnumerable<TestCaseData> MigrationProjects()
    {
        yield return new TestCaseData("src/Trax.Effect.Data.Postgres", "Postgres");
        yield return new TestCaseData("src/Trax.Effect.Data.Sqlite", "Sqlite");
    }

    [TestCaseSource(nameof(MigrationProjects))]
    public void MigrationsFolder_FilesAre_SequentiallyNumbered(
        string projectRelativePath,
        string provider
    )
    {
        var migrationsDir = RepoRoot.Combine(projectRelativePath, "Migrations");

        Directory
            .Exists(migrationsDir)
            .Should()
            .BeTrue(
                $"every Trax.Effect data provider must ship a Migrations/ folder; '{migrationsDir}' was missing."
            );

        var sqlFiles = Directory
            .EnumerateFiles(migrationsDir, "*.sql")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        sqlFiles
            .Should()
            .NotBeEmpty($"{provider} migrations folder must contain at least one .sql file.");

        var numberPattern = new Regex(@"^(?<num>\d{3})_[^/]+\.sql$", RegexOptions.Compiled);
        var seenNumbers = new List<int>();
        var malformed = new List<string>();

        foreach (var file in sqlFiles)
        {
            var match = numberPattern.Match(file!);
            if (!match.Success)
            {
                malformed.Add(file!);
                continue;
            }
            seenNumbers.Add(int.Parse(match.Groups["num"].Value));
        }

        malformed
            .Should()
            .BeEmpty(
                $"{provider} migrations must be named '<NNN>_<description>.sql' where NNN is a 3-digit "
                    + "sequence number (see CLAUDE.md > Pattern Matching > Migrations). Malformed:\n  "
                    + string.Join("\n  ", malformed)
            );

        // sequential from 1 with no gaps and no duplicates
        seenNumbers.Should().OnlyHaveUniqueItems($"{provider} migration numbers must be unique.");
        var ordered = seenNumbers.OrderBy(n => n).ToList();
        ordered[0].Should().Be(1, $"{provider} migrations must start at 001.");
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i]
                .Should()
                .Be(
                    i + 1,
                    $"{provider} migration numbers must be sequential without gaps. "
                        + $"Found {string.Join(", ", ordered)}."
                );
        }
    }

    [TestCaseSource(nameof(MigrationProjects))]
    public void MigrationsFolder_FilesAre_EmbeddedResources(
        string projectRelativePath,
        string provider
    )
    {
        var projectDir = RepoRoot.Combine(projectRelativePath);
        var csproj = Directory
            .EnumerateFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        csproj.Should().NotBeNull($"no .csproj found at {projectDir}.");

        var doc = XDocument.Load(csproj!);
        var embeddedResources = doc.Descendants("EmbeddedResource")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .Select(v => v!.Replace('\\', '/'))
            .ToList();

        var hasWildcard = embeddedResources.Any(v =>
            v.Equals("Migrations/*.sql", StringComparison.Ordinal)
            || v.Equals("Migrations/**/*.sql", StringComparison.Ordinal)
        );

        if (hasWildcard)
            return; // wildcard covers every .sql file in Migrations/

        var migrationsDir = Path.Combine(projectDir, "Migrations");
        var sqlFiles = Directory
            .EnumerateFiles(migrationsDir, "*.sql")
            .Select(Path.GetFileName)
            .ToList();

        var missing = sqlFiles
            .Where(name =>
                !embeddedResources.Any(er =>
                    er.EndsWith($"Migrations/{name}", StringComparison.Ordinal)
                )
            )
            .ToList();

        missing
            .Should()
            .BeEmpty(
                $"{provider} migration .sql files must be embedded resources or be covered by a "
                    + "<EmbeddedResource Include=\"Migrations/*.sql\" /> wildcard in the .csproj. "
                    + "Missing from EmbeddedResource:\n  "
                    + string.Join("\n  ", missing)
            );
    }
}
