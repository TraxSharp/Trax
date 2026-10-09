namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every migration Trax ships is named in the database migrations guide, or recorded here as
/// deliberately left out of it.
///
/// <para>The failure mode this exists for is an upgrade that surprises the person running it: a
/// new column their code reads, a trigger, a key that moved, or a table locked while a script
/// runs. <c>UsePostgres</c> and <c>UseSqlite</c> apply every migration at startup, so nothing
/// stops to ask, and the guide is the only place an upgrader learns what changed.</para>
///
/// <para>A migration counts as named when a heading of
/// <c>migration-guides/database-migrations.md</c> lists its number, Postgres numbers first and
/// SQLite ones after the word <c>SQLite</c> (<c>## Checkpoints (074, SQLite 036)</c>; a heading
/// that begins with <c>SQLite</c> lists SQLite numbers only), or when the page names its script
/// (<c>036_fk_and_manifest_eval_indexes</c>).</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class MigrationsAreDocumentedTests
{
    private const string Guide = "migration-guides/database-migrations.md";

    private const string BeforeTheGuard =
        "Written before this guard, and judged then to change nothing an upgrader sees.";

    /// <summary>
    /// Migrations left out of the guide, keyed by <c>Provider/script</c>, each with the reason. A
    /// new migration is left out only when it changes nothing an upgrader can see, and then with
    /// a reason of its own. An entry the guide now names, or whose script no longer exists, fails
    /// the build, so a documented migration takes its entry with it.
    /// </summary>
    private static readonly Dictionary<string, string> NotInTheGuide = new(StringComparer.Ordinal)
    {
        ["Postgres/001_train"] = BeforeTheGuard,
        ["Postgres/002_log"] = BeforeTheGuard,
        ["Postgres/003_train_input_output"] = BeforeTheGuard,
        ["Postgres/004_log_pkey"] = BeforeTheGuard,
        ["Postgres/005_metadata_external_id_cleanup"] = BeforeTheGuard,
        ["Postgres/006_manifest"] = BeforeTheGuard,
        ["Postgres/007_metadata_manifest_fk"] = BeforeTheGuard,
        ["Postgres/008_dead_letter"] = BeforeTheGuard,
        ["Postgres/009_manifest_external_id_varchar"] = BeforeTheGuard,
        ["Postgres/010_manifest_group_id"] = BeforeTheGuard,
        ["Postgres/012_manifest_depends_on"] = BeforeTheGuard,
        ["Postgres/013_priority"] = BeforeTheGuard,
        ["Postgres/015_dormant_dependent"] = BeforeTheGuard,
        ["Postgres/016_background_job"] = BeforeTheGuard,
        ["Postgres/017_concurrency_safety"] = BeforeTheGuard,
        ["Postgres/019_cancelled_state"] = BeforeTheGuard,
        ["Postgres/020_cancellation_and_step_progress"] = BeforeTheGuard,
        ["Postgres/021_log_performance"] = BeforeTheGuard,
        ["Postgres/022_misfire_policy"] = BeforeTheGuard,
        ["Postgres/023_once_schedule_type"] = BeforeTheGuard,
        ["Postgres/024_exclusions"] = BeforeTheGuard,
        ["Postgres/025_add_missing_indexes"] = BeforeTheGuard,
        ["Postgres/027_host_tracking"] = BeforeTheGuard,
        ["Postgres/028_dispatch_attempts"] = BeforeTheGuard,
        ["Postgres/029_rename_step_to_junction"] = BeforeTheGuard,
        ["Postgres/030_schedule_variance"] = BeforeTheGuard,
        ["Postgres/031_background_job_priority"] = BeforeTheGuard,
        ["Postgres/032_capacity_cleanup_indexes"] = BeforeTheGuard,
        ["Postgres/033_work_queue_dead_letter_id"] = BeforeTheGuard,
        ["Postgres/034_scheduler_config"] = BeforeTheGuard,
        ["Postgres/035_persisted_operations"] = BeforeTheGuard,
        ["Postgres/037_metrics_covering_indexes"] = BeforeTheGuard,
        ["Postgres/038_manifest_scoped_metadata_index"] = BeforeTheGuard,
        ["Postgres/039_metadata_host_rollup_index"] = BeforeTheGuard,
        ["Postgres/040_state_machine_snapshots"] = BeforeTheGuard,
        ["Postgres/041_work_queue_confirmed_at"] = BeforeTheGuard,
        ["Postgres/042_work_queue_subject_key"] = BeforeTheGuard,
        ["Postgres/043_metadata_failure_class"] = BeforeTheGuard,
        ["Postgres/044_metadata_failure_class_index"] = BeforeTheGuard,
        ["Postgres/045_work_queue_cancelled_staged_index"] = BeforeTheGuard,
        ["Postgres/047_runner_nonce"] = BeforeTheGuard,
        ["Postgres/051_snapshot_draft_machine_key"] = BeforeTheGuard,
        ["Postgres/052_scheduler_settings_and_manifest_scope"] = BeforeTheGuard,
        ["Postgres/054_decision"] = BeforeTheGuard,
        ["Sqlite/001_initial"] = BeforeTheGuard,
        ["Sqlite/002_scheduler_config"] = BeforeTheGuard,
        ["Sqlite/003_persisted_operations"] = BeforeTheGuard,
        ["Sqlite/005_metrics_covering_indexes"] = BeforeTheGuard,
        ["Sqlite/006_state_machine_snapshots"] = BeforeTheGuard,
        ["Sqlite/007_work_queue_confirmed_at"] = BeforeTheGuard,
        ["Sqlite/008_work_queue_subject_key"] = BeforeTheGuard,
        ["Sqlite/009_metadata_failure_class"] = BeforeTheGuard,
        ["Sqlite/010_work_queue_cancelled_staged_index"] = BeforeTheGuard,
        ["Sqlite/012_runner_nonce"] = BeforeTheGuard,
        ["Sqlite/015_snapshot_draft_machine_key"] = BeforeTheGuard,
        ["Sqlite/016_scheduler_settings_and_manifest_scope"] = BeforeTheGuard,
        ["Sqlite/019_decision"] = BeforeTheGuard,
    };

    private static readonly Regex Heading = new(
        @"^#{1,6} (?<text>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex Parenthesis = new(@"\((?<inner>[^)]*)\)", RegexOptions.Compiled);

    private static readonly Regex Token = new(@"SQLite|\b\d{3}\b", RegexOptions.Compiled);

    [Test]
    public void Every_Migration_IsNamed_InTheGuide()
    {
        var guide = File.ReadAllText(RepoRoot.Combine(Guide.Split('/')));
        var named = NamedNumbers(guide);

        var scripts = Scripts().ToList();
        scripts
            .Should()
            .NotBeEmpty(
                "the guard reads the shipped migrations; finding none means it checks nothing"
            );

        var missing = new List<string>();
        var leftOut = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (provider, script) in scripts)
        {
            var key = $"{provider}/{script}";
            if (
                named.Contains((provider, script[..3]))
                || guide.Contains(script, StringComparison.Ordinal)
            )
                continue;

            if (NotInTheGuide.ContainsKey(key))
                leftOut.Add(key);
            else
                missing.Add(key);
        }

        var stale = NotInTheGuide.Keys.Except(leftOut).OrderBy(k => k, StringComparer.Ordinal);

        missing
            .Should()
            .BeEmpty(
                $"every migration must be named in {Guide}: a heading listing its number (Postgres "
                    + "first, then SQLite after the word SQLite) that says what an upgrader sees, "
                    + "or the script's name on the page. A migration that changes nothing visible "
                    + "goes in NotInTheGuide with its reason instead. See "
                    + "Trax.Docs/adr/0008-documentation-conventions-are-linted.md. Not named:\n  "
                    + string.Join("\n  ", missing)
            );

        stale
            .Should()
            .BeEmpty(
                "a NotInTheGuide entry the guide now names, or whose script no longer exists, is "
                    + "stale: delete it. Stale entries:\n  "
                    + string.Join("\n  ", stale)
            );
    }

    [TestCase("## Checkpoints and resume links (074, SQLite 036)", "Postgres", "074")]
    [TestCase("## Checkpoints and resume links (074, SQLite 036)", "Sqlite", "036")]
    [TestCase("## Junction runs (055, 057 and 068; SQLite 020 and 031)", "Sqlite", "031")]
    [TestCase("## SQLite enum partial indexes (014)", "Sqlite", "014")]
    [TestCase("## Queued-work notify (069)", "Postgres", "069")]
    public void Heading_NamesTheNumber_ForItsProvider(
        string heading,
        string provider,
        string number
    ) => NamedNumbers(heading).Should().Contain((provider, number));

    [Test]
    public void Heading_DoesNotName_ASQLiteNumberForPostgres() =>
        NamedNumbers("## SQLite enum partial indexes (014)")
            .Should()
            .NotContain(("Postgres", "014"));

    /// <summary>The <c>(provider, number)</c> pairs the guide's headings list.</summary>
    private static HashSet<(string Provider, string Number)> NamedNumbers(string guide)
    {
        var named = new HashSet<(string, string)>();
        foreach (Match heading in Heading.Matches(guide))
        {
            var text = heading.Groups["text"].Value;
            foreach (Match parenthesis in Parenthesis.Matches(text))
            {
                var sqlite = text[..parenthesis.Index].Contains("SQLite", StringComparison.Ordinal);
                foreach (Match token in Token.Matches(parenthesis.Groups["inner"].Value))
                {
                    if (token.Value == "SQLite")
                        sqlite = true;
                    else
                        named.Add((sqlite ? "Sqlite" : "Postgres", token.Value));
                }
            }
        }

        return named;
    }

    /// <summary>Every shipped migration script, by provider, without its extension.</summary>
    private static IEnumerable<(string Provider, string Script)> Scripts()
    {
        var monorepo = Directory.GetParent(RepoRoot.Path)!.FullName;
        foreach (var provider in new[] { "Postgres", "Sqlite" })
        {
            var folder = Path.Combine(
                monorepo,
                "Trax.Effect",
                "src",
                $"Trax.Effect.Data.{provider}",
                "Migrations"
            );
            foreach (
                var file in Directory.EnumerateFiles(folder, "*.sql").Order(StringComparer.Ordinal)
            )
                yield return (provider, Path.GetFileNameWithoutExtension(file));
        }
    }
}
