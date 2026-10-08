using Trax.Core.Monad;

namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// <c>docs/interaction-matrix.md</c> names, for every step kind added after the current ten, the
/// test covering it with each existing feature. A row with an empty cell, or naming a test that
/// does not exist, claims coverage nobody wrote; a <see cref="ChainStepKind"/> member with no row
/// is a step kind shipped without saying how it behaves with the rest of Trax.
///
/// <para>Not ADR-enforcing: it keeps a coverage table honest, and the table is the record itself;
/// there was no choice between alternatives for an ADR to explain, only a list to keep complete.</para>
/// </summary>
[TestFixture]
public class InteractionMatrixTests
{
    private const string MatrixPath = "docs/interaction-matrix.md";

    /// <summary>
    /// The step kinds that existed when the matrix was introduced. They are covered by their own
    /// suites; every kind added after them needs a row.
    /// </summary>
    private static readonly HashSet<string> BaselineKinds = new(StringComparer.Ordinal)
    {
        nameof(ChainStepKind.Chain),
        nameof(ChainStepKind.IChain),
        nameof(ChainStepKind.ShortCircuit),
        nameof(ChainStepKind.Extract),
        nameof(ChainStepKind.Resolve),
        nameof(ChainStepKind.Seed),
        nameof(ChainStepKind.Decide),
        nameof(ChainStepKind.Switch),
        nameof(ChainStepKind.Gate),
        nameof(ChainStepKind.Scale),
    };

    private static readonly Regex TestCell = new(
        @"^`(?<class>[A-Za-z_][A-Za-z0-9_]*)\.(?<method>[A-Za-z_][A-Za-z0-9_]*)`$",
        RegexOptions.Compiled
    );

    private static readonly Regex NotApplicable = new(@"^n/a:\s*\S.{8,}$", RegexOptions.Compiled);

    [Test]
    public void TheMatrix_CoversEveryNewStepKind_WithTestsThatExist()
    {
        var problems = Problems(
            File.ReadAllText(RepoRoot.Combine(MatrixPath)),
            Enum.GetNames<ChainStepKind>(),
            new TestIndex(MonorepoRoot.Path).Exists
        );

        problems
            .Should()
            .BeEmpty(
                $"{MatrixPath} must give every step kind added after the baseline a row, each "
                    + "cell naming an existing test as `Class.Method` or saying `n/a: <why>`:\n  "
                    + string.Join("\n  ", problems)
            );
    }

    [Test]
    public void TheBaseline_NamesOnlyStepKindsThatExist() =>
        BaselineKinds
            .Except(Enum.GetNames<ChainStepKind>())
            .Should()
            .BeEmpty("a baseline kind that was renamed or removed must leave the list too");

    [Test]
    public void TheTestIndex_FindsATestInAnotherProject_AndNotOneThatIsMissing()
    {
        var index = new TestIndex(MonorepoRoot.Path);

        index
            .Exists("ChainGraphTests", "From_NumbersARepeatedJunctionByItsOccurrence")
            .Should()
            .BeTrue("the unit tests are under Trax.Core/tests, which the index reads");
        index.Exists("ChainGraphTests", "NoSuchMethod").Should().BeFalse();
        index
            .Exists("NoSuchTests", "From_NumbersARepeatedJunctionByItsOccurrence")
            .Should()
            .BeFalse();
    }

    #region The guard itself, over matrices held in memory

    private static readonly string[] Kinds = [.. BaselineKinds, "Parallel"];

    private static bool Known(string type, string method) =>
        (type, method) is ("ParallelReplayTests", "Replays") or ("ParallelCancelTests", "Cancels");

    private static string Matrix(params string[] rows) =>
        string.Join(
            "\n",
            new[]
            {
                "# Interaction matrix",
                "",
                "| Step kind | Decision replay | Dashboard cancel |",
                "|---|---|---|",
            }.Concat(rows)
        ) + "\n";

    [Test]
    public void AFullRowOfExistingTests_Passes() =>
        Problems(
                Matrix(
                    "| Parallel | `ParallelReplayTests.Replays` | `ParallelCancelTests.Cancels` |"
                ),
                Kinds,
                Known
            )
            .Should()
            .BeEmpty();

    [Test]
    public void ACellSayingWhyThePairCannotInteract_Passes() =>
        Problems(
                Matrix(
                    "| Parallel | `ParallelReplayTests.Replays` | n/a: the branch never reaches a host that can be cancelled |"
                ),
                Kinds,
                Known
            )
            .Should()
            .BeEmpty();

    [Test]
    public void AnEmptyCell_Fails() =>
        Problems(Matrix("| Parallel | `ParallelReplayTests.Replays` |  |"), Kinds, Known)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Parallel")
            .And.Contain("Dashboard cancel")
            .And.Contain("empty");

    [Test]
    public void ACellNamingATestThatDoesNotExist_Fails() =>
        Problems(
                Matrix(
                    "| Parallel | `ParallelReplayTests.Replays` | `ParallelCancelTests.Vanished` |"
                ),
                Kinds,
                Known
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("ParallelCancelTests.Vanished")
            .And.Contain("does not exist");

    [Test]
    public void ACellThatIsNeitherATestNorAReason_Fails() =>
        Problems(Matrix("| Parallel | `ParallelReplayTests.Replays` | todo |"), Kinds, Known)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'todo'");

    [Test]
    public void ARowMissingAColumn_Fails() =>
        Problems(Matrix("| Parallel | `ParallelReplayTests.Replays` |"), Kinds, Known)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("2 cells")
            .And.Contain("3");

    [Test]
    public void ANewStepKindWithNoRow_Fails() =>
        Problems(Matrix(), Kinds, Known)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("ChainStepKind.Parallel")
            .And.Contain("no row");

    [Test]
    public void ARowForAStepKindThatDoesNotExist_Fails() =>
        Problems(
                Matrix(
                    "| Parallel | `ParallelReplayTests.Replays` | `ParallelCancelTests.Cancels` |",
                    "| Fork | `ParallelReplayTests.Replays` | `ParallelCancelTests.Cancels` |"
                ),
                Kinds,
                Known
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'Fork'");

    [Test]
    public void AMatrixWithNoTable_Fails() =>
        Problems("# Interaction matrix\n", Kinds, Known)
            .Should()
            .Contain(p => p.Contains("no table"));

    #endregion

    /// <summary>
    /// Every problem with <paramref name="markdown"/> as a matrix for the step kinds
    /// <paramref name="kinds"/>, where <paramref name="testExists"/> says whether a test class
    /// declares a method.
    /// </summary>
    private static List<string> Problems(
        string markdown,
        IReadOnlyCollection<string> kinds,
        Func<string, string, bool> testExists
    )
    {
        var problems = new List<string>();
        var lines = markdown.Split('\n').Select(l => l.Trim()).ToArray();
        var start = Array.FindIndex(
            lines,
            l => l.StartsWith("| Step kind |", StringComparison.Ordinal)
        );
        if (start < 0 || start + 1 >= lines.Length)
        {
            problems.Add("the file has no table whose first column is 'Step kind'");
            return problems;
        }

        var header = Cells(lines[start]);
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = start + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            var line = i + 1;
            var cells = Cells(lines[i]);
            var kind = cells[0].Trim('`');

            if (!kinds.Contains(kind))
                problems.Add($"line {line}: '{kind}' is not a ChainStepKind member");
            else if (BaselineKinds.Contains(kind))
                problems.Add($"line {line}: '{kind}' is a baseline kind and needs no row");
            rows[kind] = line;

            if (cells.Length != header.Length)
            {
                problems.Add(
                    $"line {line}: the {kind} row has {cells.Length} cells, the header {header.Length}"
                );
                continue;
            }

            for (var c = 1; c < cells.Length; c++)
            {
                var where = $"line {line}: {kind} x {header[c]}";
                var cell = cells[c];

                if (cell.Length == 0)
                    problems.Add($"{where} is empty");
                else if (TestCell.Match(cell) is { Success: true } test)
                {
                    var (type, method) = (test.Groups["class"].Value, test.Groups["method"].Value);
                    if (!testExists(type, method))
                        problems.Add($"{where} names {type}.{method}, which does not exist");
                }
                else if (!NotApplicable.IsMatch(cell))
                    problems.Add(
                        $"{where} is '{cell}', neither `Class.Method` nor 'n/a: <why it cannot interact>'"
                    );
            }
        }

        problems.AddRange(
            kinds
                .Where(k => !BaselineKinds.Contains(k) && !rows.ContainsKey(k))
                .Select(k =>
                    $"ChainStepKind.{k} has no row: say how it behaves with each existing feature"
                )
        );

        return problems;
    }

    private static string[] Cells(string line) =>
        line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    /// <summary>
    /// Finds a test by class and method name in any folder's <c>tests/</c>: a file that declares
    /// the class and the method. Read lazily, since the real matrix names tests only once a new
    /// step kind has a row.
    /// </summary>
    private sealed class TestIndex(string monorepoRoot)
    {
        private readonly Lazy<List<string>> _sources = new(() =>
            Directory
                .EnumerateDirectories(monorepoRoot, "Trax.*")
                .Select(folder => Path.Combine(folder, "tests"))
                .Where(Directory.Exists)
                .SelectMany(tests =>
                    Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
                )
                .Where(file =>
                    !file.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")
                )
                .Select(File.ReadAllText)
                .ToList()
        );

        public bool Exists(string type, string method)
        {
            var declaresClass = new Regex($@"\bclass\s+{Regex.Escape(type)}\b");
            var declaresMethod = new Regex($@"\b{Regex.Escape(method)}\s*\(");

            return _sources.Value.Any(source =>
                declaresClass.IsMatch(source) && declaresMethod.IsMatch(source)
            );
        }
    }
}
