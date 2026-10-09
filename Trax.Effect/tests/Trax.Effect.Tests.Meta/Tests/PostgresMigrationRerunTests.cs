namespace Trax.Effect.Tests.Meta.Tests;

/// <summary>
/// Every statement in a Postgres migration from 046 on can run again over its own result, and none
/// of them builds an index on a hot table with a lock that blocks its writers.
///
/// <para>The migrator runs a script without a transaction, one statement at a time. A script that
/// stops partway keeps what it did and is not journaled, so it runs again from its first statement
/// at the next start: a statement that cannot run twice fails that start and every one after it.
/// The rules, per statement: <c>CREATE TABLE</c>/<c>INDEX</c> say <c>IF NOT EXISTS</c>,
/// <c>DROP</c> says <c>IF EXISTS</c>, <c>ADD COLUMN</c> says <c>IF NOT EXISTS</c>, a change that has
/// no such clause (a type change, a new constraint, a rename) sits in a <c>DO</c> block that checks
/// first, and an <c>INSERT</c> says what happens on a conflict. An index on <c>metadata</c>,
/// <c>log</c>, <c>work_queue</c> or <c>snapshot_draft</c> is built <c>CONCURRENTLY</c>.</para>
///
/// <para>This reads the SQL as text, so it checks that a guard is there, not that the guard is
/// right: the body of a <c>DO</c> block, and the <c>WHERE</c> of an <c>UPDATE</c> or
/// <c>DELETE</c>, are the reviewer's. <c>PostgresMigrationTests</c> runs every script again over a
/// migrated database, which is the check of what the guards do.</para>
///
/// <para>Enforces <c>docs/adr/0014-postgres-migrations-are-serialized-and-rerunnable.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0014-postgres-migrations-are-serialized-and-rerunnable.md")]
[TestFixture]
public class PostgresMigrationRerunTests
{
    private const string Adr = "docs/adr/0014-postgres-migrations-are-serialized-and-rerunnable.md";

    /// <summary>The first migration written under the rule. The ones before it shipped as they are.</summary>
    private const int FirstRerunnable = 46;

    private static readonly string[] HotTables =
    [
        "metadata",
        "log",
        "work_queue",
        "snapshot_draft",
    ];

    public static IEnumerable<TestCaseData> Scripts() =>
        Directory
            .EnumerateFiles(
                RepoRoot.Combine("src/Trax.Effect.Data.Postgres", "Migrations"),
                "*.sql"
            )
            .Select(path => Path.GetFileName(path)!)
            .Where(name => int.Parse(name[..3]) >= FirstRerunnable)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new TestCaseData(name).SetName($"{{m}}({name})"));

    [TestCaseSource(nameof(Scripts))]
    public void Every_statement_can_run_again(string fileName)
    {
        var sql = File.ReadAllText(
            RepoRoot.Combine("src/Trax.Effect.Data.Postgres", "Migrations", fileName)
        );

        var problems = Statements(sql)
            .Select(statement => (statement, problem: Problem(statement)))
            .Where(x => x.problem is not null)
            .Select(x => $"{x.problem}\n    {x.statement}")
            .ToList();

        problems
            .Should()
            .BeEmpty(
                $"{fileName} runs again from its first statement when it stops partway, so every "
                    + $"statement must be safe to repeat (see {Adr}). Unguarded:\n  "
                    + string.Join("\n  ", problems)
            );
    }

    [Test]
    public void The_splitter_sees_a_do_block_as_one_statement()
    {
        Statements(
                "-- a; comment\nDO $$ BEGIN IF true THEN PERFORM 1; END IF; END$$;\nSELECT 'a;b';"
            )
            .Should()
            .Equal("DO $$ BEGIN IF true THEN PERFORM 1; END IF; END$$", "SELECT 'a;b'");
    }

    [TestCase("CREATE INDEX ix ON trax.manifest (name)", "IF NOT EXISTS")]
    [TestCase("CREATE INDEX IF NOT EXISTS ix ON trax.work_queue (x)", "CONCURRENTLY")]
    [TestCase("CREATE UNIQUE INDEX IF NOT EXISTS ix ON trax.log (x)", "CONCURRENTLY")]
    [TestCase("CREATE UNIQUE INDEX IF NOT EXISTS ix ON trax.snapshot_draft (x)", "CONCURRENTLY")]
    [TestCase("CREATE TABLE trax.t (id int)", "IF NOT EXISTS")]
    [TestCase("DROP INDEX trax.ix", "IF EXISTS")]
    [TestCase("ALTER TABLE trax.t ADD COLUMN c int", "ADD COLUMN")]
    [TestCase("ALTER TABLE trax.t ALTER COLUMN c TYPE bigint", "DO block")]
    [TestCase("ALTER TABLE trax.t ADD CONSTRAINT k UNIQUE (c)", "DO block")]
    [TestCase("ALTER TYPE trax.e ADD VALUE 'x'", "IF NOT EXISTS")]
    [TestCase("INSERT INTO trax.t VALUES (1)", "ON CONFLICT")]
    [TestCase("DO $$ BEGIN ALTER TABLE trax.t ALTER COLUMN c TYPE bigint; END$$", "check")]
    [TestCase("GRANT ALL ON trax.t TO someone", "no rule")]
    public void Each_unguarded_form_is_refused(string statement, string reason)
    {
        Problem(statement).Should().NotBeNull().And.Contain(reason);
    }

    [TestCase("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix ON trax.metadata (external_id)")]
    [TestCase("CREATE INDEX IF NOT EXISTS ix ON trax.runner_nonce (expires_at)")]
    [TestCase("CREATE TABLE IF NOT EXISTS trax.t (id int)")]
    [TestCase("DROP INDEX CONCURRENTLY IF EXISTS trax.ix")]
    [TestCase("ALTER TABLE trax.t ADD COLUMN IF NOT EXISTS c int, ADD COLUMN IF NOT EXISTS d int")]
    [TestCase("ALTER TABLE trax.t ALTER COLUMN c SET DEFAULT now()")]
    [TestCase("ALTER TABLE trax.t ALTER COLUMN c SET NOT NULL")]
    [TestCase("ALTER TYPE trax.e ADD VALUE IF NOT EXISTS 'x'")]
    [TestCase("INSERT INTO trax.t VALUES (1) ON CONFLICT DO NOTHING")]
    [TestCase("UPDATE trax.t SET c = 1 WHERE c IS NULL")]
    [TestCase(
        "DO $$ BEGIN IF NOT EXISTS (SELECT 1) THEN CREATE TYPE trax.e AS ENUM ('a'); END IF; END$$"
    )]
    public void Each_guarded_form_is_accepted(string statement)
    {
        Problem(statement).Should().BeNull();
    }

    /// <summary>What is wrong with <paramref name="statement"/>, or null when it can run again.</summary>
    private static string? Problem(string statement)
    {
        var s = Regex.Replace(statement, @"\s+", " ").Trim().ToUpperInvariant();

        if (s.StartsWith("DO "))
            return Regex.IsMatch(s, @"\bIF\b")
                ? null
                : "a DO block must check the state before it changes it (an IF), or it is not a "
                    + "check at all";

        if (Regex.IsMatch(s, @"^CREATE (UNIQUE )?INDEX\b"))
        {
            if (!s.Contains("IF NOT EXISTS"))
                return "CREATE INDEX without IF NOT EXISTS";
            var table = Regex.Match(s, @" ON (?:ONLY )?(?:TRAX\.)?""?(\w+)""?").Groups[1].Value;
            if (HotTables.Contains(table.ToLowerInvariant()) && !s.Contains(" CONCURRENTLY "))
                return $"an index on {table.ToLowerInvariant()} must be built CONCURRENTLY: a plain "
                    + "CREATE INDEX blocks every write to the table while it builds";
            return null;
        }

        if (s.StartsWith("CREATE OR REPLACE "))
            return null;

        if (Regex.IsMatch(s, @"^CREATE (TABLE|SCHEMA|SEQUENCE|EXTENSION)\b"))
            return s.Contains("IF NOT EXISTS") ? null : "CREATE without IF NOT EXISTS";

        if (s.StartsWith("DROP "))
            return s.Contains("IF EXISTS") ? null : "DROP without IF EXISTS";

        if (s.StartsWith("ALTER TABLE "))
            return AlterTableProblem(s);

        if (s.StartsWith("ALTER TYPE "))
            return s.Contains("ADD VALUE IF NOT EXISTS")
                ? null
                : "ALTER TYPE must be ADD VALUE IF NOT EXISTS; anything else goes in a DO block that "
                    + "checks first";

        if (s.StartsWith("INSERT "))
            return s.Contains(" ON CONFLICT ") || s.Contains("NOT EXISTS")
                ? null
                : "an INSERT must say what happens ON CONFLICT, or insert WHERE NOT EXISTS";

        if (Regex.IsMatch(s, @"^(UPDATE|DELETE|SELECT|COMMENT ON|ANALYZE)\b"))
            return null;

        return "no rule covers this statement: add one here, or put it in a DO block that checks "
            + "first";
    }

    private static string? AlterTableProblem(string s)
    {
        var body = Regex.Replace(s, @"^ALTER TABLE (IF EXISTS )?(ONLY )?\S+ ", "");
        foreach (var clause in SplitTopLevel(body, ','))
        {
            var c = clause.Trim();
            if (c.StartsWith("ADD COLUMN "))
            {
                if (!c.StartsWith("ADD COLUMN IF NOT EXISTS "))
                    return "ADD COLUMN without IF NOT EXISTS";
            }
            else if (c.StartsWith("DROP "))
            {
                if (!c.Contains("IF EXISTS"))
                    return "DROP without IF EXISTS";
            }
            else if (
                Regex.IsMatch(c, @"^ALTER (COLUMN )?\S+ (SET|DROP) (DEFAULT|NOT NULL)\b")
                || Regex.IsMatch(c, @"^ALTER (COLUMN )?\S+ SET DEFAULT ")
            )
            {
                // Setting or dropping a default or NOT NULL leaves the same state however often
                // it runs.
            }
            else
                return $"'{c}' has no IF [NOT] EXISTS form: put it in a DO block that checks first";
        }
        return null;
    }

    /// <summary>
    /// Splits a script into statements on <c>;</c>, leaving out <c>--</c> comments and keeping
    /// quoted strings and <c>$$</c> bodies whole.
    /// </summary>
    private static List<string> Statements(string sql)
    {
        var statements = new List<string>();
        var current = new System.Text.StringBuilder();
        var i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                    i++;
                continue;
            }
            if (sql[i] == '\'')
            {
                var end = sql.IndexOf('\'', i + 1);
                current.Append(sql, i, end - i + 1);
                i = end + 1;
                continue;
            }
            if (sql[i] == '$' && i + 1 < sql.Length && sql[i + 1] == '$')
            {
                var end = sql.IndexOf("$$", i + 2, StringComparison.Ordinal);
                current.Append(sql, i, end - i + 2);
                i = end + 2;
                continue;
            }
            if (sql[i] == ';')
            {
                Add();
                i++;
                continue;
            }
            current.Append(sql[i]);
            i++;
        }
        Add();
        return statements;

        void Add()
        {
            var statement = Regex.Replace(current.ToString(), @"\s+", " ").Trim();
            if (statement.Length > 0)
                statements.Add(statement);
            current.Clear();
        }
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')')
                depth--;
            else if (text[i] == separator && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        yield return text[start..];
    }
}
