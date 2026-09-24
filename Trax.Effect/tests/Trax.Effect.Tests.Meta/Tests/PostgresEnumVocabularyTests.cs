using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql.NameTranslation;

namespace Trax.Effect.Tests.Meta.Tests;

/// <summary>
/// A closed vocabulary Trax persists is a Postgres enum type, and the C# enum, the three places
/// it is mapped, and the labels the migrations create stay in step.
///
/// <para>A value added to the C# enum without an <c>ALTER TYPE ... ADD VALUE</c> fails the first
/// write that uses it, and one mapping list forgotten makes that provider path read the column as
/// text. Both are silent until the value is written. Enforces
/// <c>docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md")]
[TestFixture]
public class PostgresEnumVocabularyTests
{
    private const string Adr = "docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md";

    private static readonly string Extensions = RepoRoot.Combine(
        "src",
        "Trax.Effect.Data.Postgres",
        "Extensions"
    );

    private static readonly NpgsqlSnakeCaseNameTranslator Translator = new();

    /// <summary>Enum name to Postgres type name, from the data source mapping.</summary>
    private static Dictionary<string, string> DataSourceMappings() =>
        Regex
            .Matches(
                File.ReadAllText(Path.Combine(Extensions, "ModelBuilderExtensions.cs")),
                @"npgsqlDataSourceBuilder\.MapEnum<(\w+)>\(""trax\.(\w+)""\)"
            )
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    [Test]
    public void Every_mapping_list_names_the_same_enums()
    {
        var dataSource = DataSourceMappings().Keys.ToHashSet();

        var model = Regex
            .Matches(
                File.ReadAllText(Path.Combine(Extensions, "ModelBuilderExtensions.cs")),
                @"HasPostgresEnum<(\w+)>"
            )
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var options = Regex
            .Matches(
                File.ReadAllText(Path.Combine(Extensions, "ServiceExtensions.cs")),
                @"o\.MapEnum<(\w+)>\(""(\w+)"",\s*""trax""\)"
            )
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        dataSource.Should().NotBeEmpty();
        model
            .Should()
            .BeEquivalentTo(dataSource, $"HasPostgresEnum must list every mapped enum. See {Adr}");
        options
            .Should()
            .BeEquivalentTo(
                dataSource,
                $"the EF options must map every enum the data source maps. See {Adr}"
            );
    }

    [Test]
    public void Every_mapped_enum_matches_the_labels_its_migrations_create()
    {
        var sql = string.Join(
            "\n",
            Directory
                .EnumerateFiles(
                    RepoRoot.Combine("src", "Trax.Effect.Data.Postgres", "Migrations"),
                    "*.sql"
                )
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(File.ReadAllText)
        );

        var mismatches = new List<string>();

        foreach (var (enumName, pgName) in DataSourceMappings())
        {
            var labels = new HashSet<string>(StringComparer.Ordinal);

            foreach (
                Match create in Regex.Matches(
                    sql,
                    $@"create\s+type\s+trax\.{pgName}\s+as\s+enum\s*\((.*?)\)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            )
            foreach (Match label in Regex.Matches(create.Groups[1].Value, "'([^']+)'"))
                labels.Add(label.Groups[1].Value);

            foreach (
                Match added in Regex.Matches(
                    sql,
                    $@"alter\s+type\s+trax\.{pgName}\s+add\s+value\s+(?:if\s+not\s+exists\s+)?'([^']+)'",
                    RegexOptions.IgnoreCase
                )
            )
                labels.Add(added.Groups[1].Value);

            var members = ResolveEnum(enumName)
                .GetEnumNames()
                .Select(Translator.TranslateMemberName)
                .ToHashSet(StringComparer.Ordinal);

            if (!labels.SetEquals(members))
                mismatches.Add(
                    $"{enumName} -> trax.{pgName}: C# [{string.Join(", ", members.Order())}] "
                        + $"vs migrations [{string.Join(", ", labels.Order())}]"
                );
        }

        mismatches
            .Should()
            .BeEmpty(
                "each C# member needs a Postgres label, added with ALTER TYPE ... ADD VALUE in a "
                    + $"migration released before any writer uses it. See {Adr}"
            );
    }

    private static Type ResolveEnum(string name)
    {
        var candidates = new[]
        {
            typeof(Trax.Effect.Enums.TrainState).Assembly,
            typeof(Trax.Core.Exceptions.FailureClass).Assembly,
            typeof(Microsoft.Extensions.Logging.LogLevel).Assembly,
        }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsEnum && t.Name == name)
            .ToList();

        candidates.Should().ContainSingle($"the mapped enum {name} must resolve to one type");
        return candidates[0];
    }
}
