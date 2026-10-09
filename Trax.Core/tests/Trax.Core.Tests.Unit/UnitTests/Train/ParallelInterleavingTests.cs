using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Tests.Unit.Utils;
using Trax.Core.Train;
using static Trax.Core.Tests.Unit.Utils.ChainShapes;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// <c>Parallel</c> under every interleaving of its branches' junctions, compared with running the
/// same branches one after another. A test runner holds each branch before each junction and lets
/// one go at a time, so every order the thread pool could produce is produced on purpose, and
/// replayed, rather than hoped for.
///
/// <para>The equivalence is: the same value and the same Memory at the end of a run that
/// succeeds, and for a run that fails, the same set of failed branch paths. Under
/// <see cref="BranchFailurePolicy.WaitForAll"/> that is the whole of it, and the branches stopped
/// by a sibling are none in both. Under <see cref="BranchFailurePolicy.CancelSiblings"/> a
/// concurrent run is equivalent to the sequential one only up to which siblings had already
/// finished when the failure came: those that had are not stopped, those that had not are. So the
/// stopped set is not compared with the oracle's; it is predicted from the interleaving
/// instead.</para>
///
/// <para>Enforces Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md"
)]
public class ParallelInterleavingTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md";

    /// <summary>Long enough never to be reached by a run that is working, so it only names a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    #region Every interleaving

    /// <summary>
    /// Branch lengths in junctions. Every branch is held before its start and before each
    /// junction, so a branch of n junctions is n + 1 steps, and the interleavings of a step are
    /// the multinomial of those: 6, 20, 90 and 210 with no failure.
    /// </summary>
    [TestCase(new[] { 1, 1 })]
    [TestCase(new[] { 2, 2 })]
    [TestCase(new[] { 1, 1, 1 })]
    [TestCase(new[] { 1, 2, 2 })]
    public async Task EveryInterleaving_WithEveryFailurePoint_MatchesRunningTheBranchesInOrder(
        int[] lengths
    )
    {
        foreach (var policy in Enum.GetValues<BranchFailurePolicy>())
        {
            var chain = Flat(lengths, policy);
            var branches = lengths.Select((_, i) => $"Parallel#0/b{i}").ToList();
            var faultPoints = branches
                .SelectMany((b, i) => Enumerable.Range(0, lengths[i]).Select(k => (b, k)))
                .Select(p => ((string Branch, int Index)?)p)
                .Prepend(null)
                .ToList();
            var mismatches = new List<string>();
            var runs = 0;

            foreach (var fault in faultPoints)
            {
                var inject = Inject(fault);
                var oracle = await Run(chain, new SequentialBranchRunner(inject));
                var expected = Expected(chain, "x");

                var explored = await Interleavings.Every(runner => Run(chain, runner), inject);
                runs += explored.Count;

                if (fault is null)
                    explored
                        .Count.Should()
                        .Be(
                            Multinomial(lengths.Select(n => n + 1)),
                            "the harness reaches every interleaving of the branches' steps"
                        );

                foreach (var run in explored)
                {
                    var where =
                        $"[{policy}] fault {fault?.ToString() ?? "none"}, "
                        + $"order {string.Join(" ", run.Runner.Trace)}";

                    foreach (var difference in Differences(run.Result, oracle))
                        mismatches.Add($"{where}: {difference}");

                    if (run.Result.IsRight && run.Result.Snapshot != expected.Snapshot)
                        mismatches.Add(
                            $"{where}: Memory {run.Result.Snapshot}, not {expected.Snapshot}"
                        );

                    if (fault is not { } f || run.Result.Failure is not BranchesFailedException)
                        continue;

                    // Which siblings are stopped is the one thing an interleaving may change, and
                    // it is decided: exactly those that had not finished when the failing branch
                    // did.
                    var finished = run.Runner.Finished.ToList();
                    var predicted =
                        policy == BranchFailurePolicy.WaitForAll
                            ? []
                            : branches
                                .Except(finished.Take(finished.IndexOf(f.Branch)))
                                .Where(b => b != f.Branch)
                                .ToHashSet();

                    if (!run.Result.Stopped.SetEquals(predicted))
                        mismatches.Add(
                            $"{where}: stopped [{string.Join(", ", run.Result.Stopped.Order())}], "
                                + $"not [{string.Join(", ", predicted.Order())}]"
                        );
                }
            }

            TestContext.Out.WriteLine(
                $"{string.Join(",", lengths)} [{policy}]: {runs} runs over {faultPoints.Count} failure points"
            );
            mismatches
                .Should()
                .BeEmpty(
                    $"a concurrent Parallel is equivalent to its branches run in order ({Adr})"
                );
        }
    }

    [Test]
    public async Task CancellingTheRun_AtAnyJunction_OfAnyInterleaving_FailsWithTheCancellation()
    {
        int[] lengths = [1, 2, 2];
        var source = new StrongBox<CancellationTokenSource>();
        var outcomes = new List<(string Where, Outcome Outcome)>();

        foreach (var policy in Enum.GetValues<BranchFailurePolicy>())
        foreach (
            var at in lengths.SelectMany(
                (n, i) => Enumerable.Range(0, n).Select(k => ($"Parallel#0/b{i}", k))
            )
        )
        {
            var chain = Flat(lengths, policy);
            var explored = await Interleavings.Every(
                async runner =>
                {
                    using var cts = source.Value = new CancellationTokenSource();
                    var train = new ShapeTrain(chain, new RunLog())
                    {
                        BranchRunner = runner,
                        CancellationToken = cts.Token,
                    };
                    return await Outcome.Of(train, "x").WaitAsync(Hang);
                },
                (branch, index) =>
                {
                    if ((branch, index) == at)
                        source.Value!.Cancel();
                }
            );

            outcomes.AddRange(explored.Select(e => ($"[{policy}] at {at}", e.Result)));
        }

        outcomes.Should().HaveCountGreaterThan(100);
        outcomes
            .Where(o => !o.Outcome.Cancelled)
            .Select(o => $"{o.Where}: {o.Outcome}")
            .Should()
            .BeEmpty(
                $"a cancelled run fails with the cancellation, never with its branches' failures ({Adr})"
            );
    }

    [Test]
    public async Task ReplayingTheSameChoices_GivesTheSameOrder()
    {
        var chain = Flat([2, 2], BranchFailurePolicy.WaitForAll);

        var explored = await Interleavings.Every(runner => Run(chain, runner));

        foreach (var run in explored)
        {
            var at = 0;
            var replay = new ScheduledBranchRunner(_ => run.Choices[at++]);
            await Run(chain, replay);

            replay
                .Trace.Should()
                .Equal(run.Runner.Trace, "an interleaving is named by its choices alone");
        }

        explored
            .Select(e => string.Join(" ", e.Runner.Trace))
            .Should()
            .OnlyHaveUniqueItems("every sequence of choices is a different interleaving");
    }

    #endregion

    #region The harness sees bugs

    [Test]
    public async Task TheSuite_FindsTheRace_InTwoBranchesSharingOneDictionary()
    {
        // Both branches read a count and write it back plus one, through junction instances
        // handed the same dictionary. In order, the count ends at 2; interleaved as read, read,
        // write, write, one increment is lost. The suite has to find that order.
        var oracle = await Outcome.Of(
            new RacyTrain(new Dictionary<string, int> { ["n"] = 0 })
            {
                BranchRunner = new SequentialBranchRunner(),
            },
            "x"
        );

        var explored = await Interleavings.Every(runner =>
            Outcome.Of(
                new RacyTrain(new Dictionary<string, int> { ["n"] = 0 }) { BranchRunner = runner },
                "x"
            )
        );

        oracle.Result.ValueUnsafe().Should().Be("2");
        explored
            .Where(e => Differences(e.Result, oracle).Any())
            .Should()
            .NotBeEmpty("the interleaving suite must be able to see a race, or it proves nothing")
            .And.Contain(e => e.Result.Result.ValueUnsafe() == "1");
    }

    #endregion

    #region Budgets

    [Test]
    public async Task ABlockingJunction_DoesNotStopItsSiblingFromFinishing()
    {
        // The first branch blocks its thread until the second has run. Started on the caller's
        // thread rather than the pool, the second would never start.
        var signal = new TaskCompletionSource();

        var result = await new BlockingTrain(signal, Hang).RunEither("x").WaitAsync(Hang * 2);

        result.IsRight.Should().BeTrue($"branches start on the thread pool ({Adr})");
    }

    [Test]
    public async Task ABlockingJunction_RunInOrder_DoesStopItsSibling_SoTheBudgetCanFail()
    {
        var signal = new TaskCompletionSource();

        // negative-wait: the blocked branch has to give up for the run to end at all.
        var result = await new BlockingTrain(signal, TimeSpan.FromMilliseconds(200))
        {
            BranchRunner = new SequentialBranchRunner(),
        }
            .RunEither("x")
            .WaitAsync(Hang);

        result
            .Swap()
            .ValueUnsafe()
            .Should()
            .BeOfType<BranchesFailedException>()
            .Which.Failures.Should()
            .ContainSingle(f => f.Exception is TimeoutException);
    }

    [Test]
    public async Task FiftyTrainsOfFourBranches_RunAllTwoHundredBranchesAtOnce()
    {
        // No branch finishes until all two hundred have started, and each waits without holding a
        // thread. If a branch, or a join, held a thread while it waited, the pool would have to
        // grow to two hundred threads one slow injection at a time before any of them finished.
        const int trains = 50;
        var meeting = new Meeting(trains * 4);

        var results = await Task.WhenAll(
                Enumerable.Range(0, trains).Select(_ => new FourBranchTrain(meeting).RunEither("x"))
            )
            .WaitAsync(Hang);

        results.Should().OnlyContain(r => r.IsRight, $"branches wait without a thread ({Adr})");
        meeting.Arrived.Should().Be(trains * 4);
    }

    #endregion

    #region Helpers

    private static IReadOnlyList<Element> Flat(int[] lengths, BranchFailurePolicy policy) =>
        Number([
            new Fork(
                lengths
                    .Select(n => new Branch(
                        "",
                        Enumerable.Range(0, n).Select(_ => (Element)new Link(false)).ToList()
                    ))
                    .ToList(),
                policy
            ),
        ]);

    private static Action<string, int>? Inject((string Branch, int Index)? fault) =>
        fault is not { } f
            ? null
            : (branch, index) =>
            {
                if ((branch, index) == f)
                    throw new InvalidOperationException($"injected at {branch}#{index}");
            };

    private static Task<Outcome> Run(IReadOnlyList<Element> chain, IBranchRunner runner) =>
        Outcome
            .Of(new ShapeTrain(chain, new RunLog()) { BranchRunner = runner }, "x")
            .WaitAsync(Hang);

    /// <summary>How a run differs from the oracle, in the terms the equivalence compares.</summary>
    private static IEnumerable<string> Differences(Outcome run, Outcome oracle)
    {
        if (run.IsRight != oracle.IsRight)
            yield return $"{run}, where in order it is {oracle}";
        else if (run.IsRight)
        {
            if (run.Result.ValueUnsafe() != oracle.Result.ValueUnsafe())
                yield return $"returned {run.Result.ValueUnsafe()}, not {oracle.Result.ValueUnsafe()}";
            if (run.Snapshot != oracle.Snapshot)
                yield return $"Memory {run.Snapshot}, not {oracle.Snapshot}";
        }
        else if (!run.FailedBranches.SetEquals(oracle.FailedBranches))
            yield return $"{run}, where in order it is {oracle}";
    }

    private static int Multinomial(IEnumerable<int> parts)
    {
        // (a + b + ...)! / (a! b! ...), built up one binomial at a time.
        var (total, result) = (0, 1L);
        foreach (var part in parts)
            for (var i = 1; i <= part; i++)
                result = result * ++total / i;
        return (int)result;
    }

    public sealed record ReadA(int N);

    public sealed record ReadB(int N);

    public sealed record WroteA(int N);

    public sealed record WroteB(int N);

    private sealed class ReadsCount<TOut>(Dictionary<string, int> shared, Func<int, TOut> make)
        : Junction<string, TOut>
    {
        public override Task<TOut> Run(string input) => Task.FromResult(make(shared["n"]));
    }

    private sealed class WritesCount<TIn, TOut>(
        Dictionary<string, int> shared,
        Func<TIn, int> read,
        Func<int, TOut> make
    ) : Junction<TIn, TOut>
    {
        public override Task<TOut> Run(TIn input)
        {
            shared["n"] = read(input) + 1;
            return Task.FromResult(make(shared["n"]));
        }
    }

    private sealed class Total(Dictionary<string, int> shared) : Junction<(WroteA, WroteB), string>
    {
        public override Task<string> Run((WroteA, WroteB) input) =>
            Task.FromResult(shared["n"].ToString());
    }

    private sealed class RacyTrain(Dictionary<string, int> shared) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain(new ReadsCount<string>(shared, n => n.ToString()))
                .Parallel(p =>
                    p.Branch(
                            "a",
                            b =>
                                b.Chain(new ReadsCount<ReadA>(shared, n => new ReadA(n)))
                                    .Chain(
                                        new WritesCount<ReadA, WroteA>(
                                            shared,
                                            r => r.N,
                                            n => new WroteA(n)
                                        )
                                    )
                        )
                        .Branch(
                            "b",
                            b =>
                                b.Chain(new ReadsCount<ReadB>(shared, n => new ReadB(n)))
                                    .Chain(
                                        new WritesCount<ReadB, WroteB>(
                                            shared,
                                            r => r.N,
                                            n => new WroteB(n)
                                        )
                                    )
                        )
                )
                .Chain(new Total(shared))
                .Resolve();
    }

    // Blocks with Task.Wait, which the thread pool sees: it adds a thread at once for a pool thread
    // blocked that way, so the sibling starts even when other work in the process holds every pool
    // thread, instead of waiting for the pool's slow starvation injection.
    private sealed class BlocksUntil(TaskCompletionSource signal, TimeSpan wait)
        : Junction<string, ReadA>
    {
        public override Task<ReadA> Run(string input) =>
            signal.Task.Wait(wait)
                ? Task.FromResult(new ReadA(1))
                : throw new TimeoutException("the sibling never ran");
    }

    private sealed class Releases(TaskCompletionSource signal) : Junction<string, ReadB>
    {
        public override Task<ReadB> Run(string input)
        {
            signal.TrySetResult();
            return Task.FromResult(new ReadB(1));
        }
    }

    private sealed class BlockingTrain(TaskCompletionSource signal, TimeSpan wait)
        : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("blocks", b => b.Chain(new BlocksUntil(signal, wait)))
                        .Branch("releases", b => b.Chain(new Releases(signal)))
                        .OnFailure(BranchFailurePolicy.WaitForAll)
                )
                .Resolve();
    }

    private sealed class Meeting(int expected)
    {
        private int _arrived;
        private readonly TaskCompletionSource _everyone = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int Arrived => Volatile.Read(ref _arrived);

        public Task Arrive()
        {
            if (Interlocked.Increment(ref _arrived) == expected)
                _everyone.SetResult();
            return _everyone.Task;
        }
    }

    private sealed class Meets<TOut>(Meeting meeting, TOut value) : Junction<string, TOut>
    {
        public override async Task<TOut> Run(string input)
        {
            await meeting.Arrive();
            return value;
        }
    }

    private sealed class FourBranchTrain(Meeting meeting) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("a", b => b.Chain(new Meets<ReadA>(meeting, new ReadA(1))))
                        .Branch("b", b => b.Chain(new Meets<ReadB>(meeting, new ReadB(1))))
                        .Branch("c", b => b.Chain(new Meets<WroteA>(meeting, new WroteA(1))))
                        .Branch("d", b => b.Chain(new Meets<WroteB>(meeting, new WroteB(1))))
                )
                .Resolve();
    }

    private sealed class Echo : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    #endregion
}
