using System.Text.RegularExpressions;
using AwesomeAssertions;
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
public partial class PostgresEnumVocabularyTests
{
    private const string Adr = "docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md";

    private static readonly string Extensions = RepoRoot.Combine(
        "src",
        "Trax.Effect.Data.Postgres",
        "Extensions"
    );

    private static readonly NpgsqlSnakeCaseNameTranslator Translator = new();

    /// <summary>Enum name to Postgres type name, from the data source mapping.</summary>
    private static async Task<Dictionary<string, string>> DataSourceMappings() =>
        DataSourceMapEnum
            .Matches(
                await File.ReadAllTextAsync(Path.Combine(Extensions, "ModelBuilderExtensions.cs"))
                    .ConfigureAwait(false)
            )
            .ToDictionary(
                m => m.Groups["enum"].Value,
                m => m.Groups["type"].Value,
                StringComparer.Ordinal
            );

    [Test]
    public async Task Every_mapping_list_names_the_same_enums()
    {
        var dataSource = (await DataSourceMappings().ConfigureAwait(false)).Keys.ToHashSet(
            StringComparer.Ordinal
        );

        var model = PostgresEnumDeclaration
            .Matches(
                await File.ReadAllTextAsync(Path.Combine(Extensions, "ModelBuilderExtensions.cs"))
                    .ConfigureAwait(false)
            )
            .Select(m => m.Groups["enum"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var options = MapEnumOption
            .Matches(
                await File.ReadAllTextAsync(Path.Combine(Extensions, "ServiceExtensions.cs"))
                    .ConfigureAwait(false)
            )
            .Select(m => m.Groups["enum"].Value)
            .ToHashSet(StringComparer.Ordinal);

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
    public async Task Every_mapped_enum_matches_the_labels_its_migrations_create()
    {
        var sql = string.Join(
            '\n',
            Directory
                .EnumerateFiles(
                    RepoRoot.Combine("src", "Trax.Effect.Data.Postgres", "Migrations"),
                    "*.sql"
                )
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText)
        );

        var mismatches = new List<string>();

        foreach (var (enumName, pgName) in await DataSourceMappings().ConfigureAwait(false))
        {
            var labels = new HashSet<string>(StringComparer.Ordinal);

            foreach (
                Match create in Regex.Matches(
                    sql,
                    $@"create\s+type\s+trax\.{pgName}\s+as\s+enum\s*\((.*?)\)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline,
                    TimeSpan.FromSeconds(1)
                )
            )
            foreach (Match label in EnumLabel.Matches(create.Groups[1].Value))
                labels.Add(label.Groups["label"].Value);

            foreach (
                Match added in Regex.Matches(
                    sql,
                    $@"alter\s+type\s+trax\.{pgName}\s+add\s+value\s+(?:if\s+not\s+exists\s+)?'([^']+)'",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1)
                )
            )
                labels.Add(added.Groups[1].Value);

            var members = ResolveEnum(enumName)
                .GetEnumNames()
                .Select(Translator.TranslateMemberName)
                .ToHashSet(StringComparer.Ordinal);

            if (!labels.SetEquals(members))
                mismatches.Add(
                    $"{enumName} -> trax.{pgName}: C# [{string.Join(", ", members.Order(StringComparer.Ordinal))}] "
                        + $"vs migrations [{string.Join(", ", labels.Order(StringComparer.Ordinal))}]"
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
            .Where(t => t.IsEnum && string.Equals(t.Name, name, StringComparison.Ordinal))
            .ToList();

        candidates.Should().ContainSingle($"the mapped enum {name} must resolve to one type");
        return candidates[0];
    }

    [GeneratedRegex(
        @"npgsqlDataSourceBuilder\.MapEnum<(?<enum>\w+)>\(""trax\.(?<type>\w+)""\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex DataSourceMapEnum { get; }

    [GeneratedRegex(
        @"HasPostgresEnum<(?<enum>\w+)>",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex PostgresEnumDeclaration { get; }

    [GeneratedRegex("'(?<label>[^']+)'", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EnumLabel { get; }

    [GeneratedRegex(
        @"o\.MapEnum<(?<enum>\w+)>\(""\w+"",\s*""trax""\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex MapEnumOption { get; }
}
