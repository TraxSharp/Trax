using AwesomeAssertions;
using CsCheck;
using Trax.Core.Monad;
using Trax.Core.Tests.Unit.Utils;
using static Trax.Core.Tests.Unit.Utils.ChainShapes;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// The laws <c>Parallel</c> keeps for any chain, checked over generated chains rather than the
/// trains someone thought to write: linear runs, any number of branches, nested <c>Parallel</c>s,
/// <c>Parallel</c> inside a <c>Switch</c> track, a <c>Decide</c> and <c>Switch</c> inside a branch,
/// with junctions that fail at random.
///
/// <para>Every run is checked against an oracle that reads the chain's description rather than
/// running it (<see cref="ChainShapes.Expected"/>): a run succeeds exactly when no reachable
/// junction fails, ends with exactly the values the reachable junctions produce, and fails
/// carrying every failure that happened outside a branch a sibling stopped. Under
/// <see cref="BranchFailurePolicy.WaitForAll"/> that is every reachable failure; under
/// <see cref="BranchFailurePolicy.CancelSiblings"/> it is the ones that ran before the siblings
/// were stopped, which is why the laws below compare failures exactly only when no step cancels
/// siblings.</para>
///
/// <para>Each law is checked first over a <see cref="ScheduledBranchRunner"/> whose choices come
/// from a generated seed, so a failure there is deterministic: the seed CsCheck shrank it to
/// replays the input and the interleaving, and the message names the choices taken. Pin it as its
/// own test with <c>Sample(..., seed: "...")</c> before fixing the code, so the case stays
/// covered. The same law is then checked over the thread pool, as a smoke signal for interleavings
/// the scheduled runner cannot make; a failure only that run shows says the seed may not
/// reproduce it.</para>
///
/// <para>Enforces Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md"
)]
public class ParallelLawsTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md";

    /// <summary>Long enough never to be reached by a run that is working, so it only names a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    #region Concurrency

    [Test]
    public Task AConcurrentRun_IsEquivalentToRunningTheBranchesInOrder() =>
        Gen.Select(AShape(depth: 3), Gen.Int)
            .SampleAsync(
                async Task (IReadOnlyList<Element> chain, int seed) =>
                {
                    var inOrder = await Run(chain, new SequentialBranchRunner());
                    var scheduled = await RunScheduled(chain, seed);

                    Violations(chain, inOrder.Outcome, inOrder.Log)
                        .Should()
                        .BeEmpty($"every run keeps the laws of Parallel ({Adr})");
                    Violations(chain, scheduled.Outcome, scheduled.Log)
                        .Should()
                        .BeEmpty(
                            $"every run keeps the laws of Parallel ({Adr}); {scheduled.Replay}"
                        );
                    Agrees(chain, scheduled.Outcome, inOrder.Outcome, scheduled.Replay);

                    await OnThePool(
                        async () =>
                        {
                            var pool = await Run(chain, ThreadPoolBranchRunner.Instance);
                            Violations(chain, pool.Outcome, pool.Log)
                                .Should()
                                .BeEmpty($"every run keeps the laws of Parallel ({Adr})");
                            Agrees(chain, pool.Outcome, inOrder.Outcome, "on the thread pool");
                        },
                        scheduled.Replay
                    );
                },
                iter: 1000,
                print: t => $"seed {t.Item2}: {Describe(t.Item1)}"
            );

    [Test]
    public Task RunEither_NeverThrows_AndGivesOneResult_WhereverTheRunIsCancelled() =>
        AShape()
            .SelectMany(chain => Gen.Int[0, Steps(chain)].Select(at => (chain, at)))
            .Select(Gen.Int, (t, seed) => (t.chain, t.at, seed))
            .SampleAsync(
                async Task (t) =>
                {
                    var (chain, at, seed) = t;
                    var choices = new Choices(seed);
                    (await KeepsTheLawsWhenCancelled(chain, at, choices.Runner()))
                        .Should()
                        .BeEmpty(choices.ToString());

                    await OnThePool(
                        async () =>
                            (
                                await KeepsTheLawsWhenCancelled(
                                    chain,
                                    at,
                                    ThreadPoolBranchRunner.Instance
                                )
                            )
                                .Should()
                                .BeEmpty("on the thread pool"),
                        choices.ToString()
                    );
                },
                iter: 1000,
                print: t => $"seed {t.seed}, cancel at {t.at}: {Describe(t.chain)}"
            );

    /// <summary>
    /// Runs <paramref name="chain"/> cancelled at step <paramref name="at"/>, and returns where a
    /// run that was not cancelled after all breaks the laws it keeps uncancelled.
    /// </summary>
    private static async Task<List<string>> KeepsTheLawsWhenCancelled(
        IReadOnlyList<Element> chain,
        int at,
        IBranchRunner runner
    )
    {
        using var cts = new CancellationTokenSource();
        var log = new RunLog { CancelAt = (at, cts) };
        var train = new ShapeTrain(chain, log)
        {
            CancellationToken = cts.Token,
            BranchRunner = runner,
        };

        var outcome = await Outcome.Of(train, "x").WaitAsync(Hang);

        return outcome.Cancelled
            ? []
            : Violations(chain, outcome, log)
                .Select(v =>
                    $"{v} (a run is cancelled, or keeps the laws it keeps uncancelled, {Adr})"
                )
                .ToList();
    }

    #endregion

    #region Shape

    [Test]
    public Task AParallelOfOneBranch_IsThatBranchsChain() =>
        Gen.Select(AChain(1, 4, 8), Gen.Int)
            .SampleAsync(
                async Task (IReadOnlyList<Element> body, int seed) =>
                {
                    var alone = Number(body);
                    var wrapped = Number([
                        new Fork([new Branch("", body)], BranchFailurePolicy.WaitForAll),
                    ]);

                    var plain = await Run(alone, new SequentialBranchRunner());
                    var parallel = await RunScheduled(wrapped, seed);
                    Same(
                        alone,
                        plain.Outcome,
                        parallel.Outcome,
                        $"Parallel(a) is a; {parallel.Replay}"
                    );

                    await OnThePool(
                        async () =>
                        {
                            var pool = await Run(wrapped, ThreadPoolBranchRunner.Instance);
                            Same(alone, plain.Outcome, pool.Outcome, "Parallel(a) is a");
                        },
                        parallel.Replay
                    );
                },
                iter: 500,
                print: t => $"seed {t.Item2}: {Describe(Number(t.Item1))}"
            );

    [Test]
    public Task ANestedParallel_FlattensIntoItsParent() =>
        Gen.Select(AChain(1, 3, 8), AChain(1, 3, 8), AChain(1, 3, 8), Gen.Int)
            .SampleAsync(
                async Task (
                    IReadOnlyList<Element> a,
                    IReadOnlyList<Element> b,
                    IReadOnlyList<Element> c,
                    int seed
                ) =>
                {
                    var nested = Number([
                        new Fork(
                            [
                                new Branch("", a),
                                new Branch(
                                    "",
                                    [
                                        new Fork(
                                            [new Branch("", b), new Branch("", c)],
                                            BranchFailurePolicy.WaitForAll
                                        ),
                                    ]
                                ),
                            ],
                            BranchFailurePolicy.WaitForAll
                        ),
                    ]);
                    var flat = Number([
                        new Fork(
                            [new Branch("", a), new Branch("", b), new Branch("", c)],
                            BranchFailurePolicy.WaitForAll
                        ),
                    ]);
                    const string law =
                        "Parallel(a, Parallel(b, c)) is Parallel(a, b, c), but for node ids";

                    var one = await RunScheduled(nested, seed);
                    var other = await RunScheduled(flat, seed);
                    var replay = $"nested {one.Replay}; flat {other.Replay}";
                    Same(flat, one.Outcome, other.Outcome, $"{law}; {replay}");

                    await OnThePool(
                        async () =>
                        {
                            var poolOne = await Run(nested, ThreadPoolBranchRunner.Instance);
                            var poolOther = await Run(flat, ThreadPoolBranchRunner.Instance);
                            Same(flat, poolOne.Outcome, poolOther.Outcome, law);
                        },
                        replay
                    );
                },
                iter: 500,
                print: t =>
                    $"seed {t.Item4}; a: {Describe(Number(t.Item1))}; b: {Describe(Number(t.Item2))}; c: {Describe(Number(t.Item3))}"
            );

    [Test]
    public Task TheOrderBranchesAreDeclaredIn_DoesNotChangeTheResult() =>
        AFork(2, 8)
            .Select(fork => Number([fork]))
            .SelectMany(chain =>
                Gen.Shuffle(((Fork)chain[0]).Branches.ToArray())
                    .Select(shuffled => (chain, shuffled))
            )
            .Select(Gen.Int, (t, seed) => (t.chain, t.shuffled, seed))
            .SampleAsync(
                async Task (t) =>
                {
                    var (chain, shuffled, seed) = t;
                    var reordered =
                        (IReadOnlyList<Element>)[((Fork)chain[0]) with { Branches = shuffled }];

                    var declared = await RunScheduled(chain, seed);
                    var swapped = await RunScheduled(reordered, seed);
                    var replay = $"declared {declared.Replay}; swapped {swapped.Replay}";
                    Same(chain, declared.Outcome, swapped.Outcome, $"the join commutes; {replay}");

                    await OnThePool(
                        async () =>
                        {
                            var poolDeclared = await Run(chain, ThreadPoolBranchRunner.Instance);
                            var poolSwapped = await Run(reordered, ThreadPoolBranchRunner.Instance);
                            Same(
                                chain,
                                poolDeclared.Outcome,
                                poolSwapped.Outcome,
                                "the join commutes"
                            );
                        },
                        replay
                    );
                },
                iter: 500,
                print: t => $"seed {t.seed}: {Describe(t.chain)}"
            );

    [Test]
    public Task GraphIds_StayUnique_AndEveryJunctionThatRunsReportsOneOfThem() =>
        Gen.Select(AShape(), Gen.Int)
            .SampleAsync(
                async Task (IReadOnlyList<Element> chain, int seed) =>
                {
                    var declared = new ShapeTrain(chain, new RunLog()).DeclaredChain();
                    var graph = ChainGraph.From(
                        declared,
                        typeof(ShapeTrain),
                        typeof(string),
                        typeof(string)
                    );
                    var ids = Ids(graph.Nodes).ToList();

                    declared.Refusals.Should().BeEmpty();
                    ChainVerification
                        .Verify(declared, typeof(string), typeof(string))
                        .Should()
                        .BeEmpty("a generated chain is one the startup check accepts");
                    ids.Should().OnlyHaveUniqueItems($"a node id names one step ({Adr})");

                    var scheduled = await RunScheduled(chain, seed);
                    EveryJunctionReportsItsNode(ids, scheduled.Log, scheduled.Replay);

                    await OnThePool(
                        async () =>
                        {
                            var (_, log) = await Run(chain, ThreadPoolBranchRunner.Instance);
                            EveryJunctionReportsItsNode(ids, log, "on the thread pool");
                        },
                        scheduled.Replay
                    );
                },
                iter: 500,
                print: t => $"seed {t.Item2}: {Describe(t.Item1)}"
            );

    private static void EveryJunctionReportsItsNode(List<string> ids, RunLog log, string run)
    {
        foreach (var (_, node, branch) in log.Ran)
        {
            ids.Should().Contain(node!, $"a running junction says which node it is; {run}");
            if (branch is not null)
                node.Should().StartWith(branch + "/", $"a branch's steps are inside it; {run}");
        }
    }

    #endregion

    #region The join's merge

    [Test]
    public void TheMerge_DoesNotDependOnTheOrderOfTheBranches() => Commutes(BranchMerge.Added);

    [Test]
    public void TheCommutativityLaw_RefusesAMergeWhereTheLastWriterWins()
    {
        // A merge that lets the last branch's value win for a type two branches added. The law
        // has to reject it, or passing it says nothing about the real merge.
        var act = () => Commutes(LastWriterWins);

        act.Should().Throw<Exception>("the law must be able to see an order-dependent merge");
    }

    private static Dictionary<Type, object> LastWriterWins(
        IReadOnlyDictionary<Type, object> fork,
        IEnumerable<IReadOnlyDictionary<Type, object>> branches
    )
    {
        var merged = new Dictionary<Type, object>();
        foreach (var branch in branches)
        foreach (var (type, value) in branch)
            if (!fork.TryGetValue(type, out var before) || !ReferenceEquals(before, value))
                merged[type] = value;
        return merged;
    }

    private sealed record Box(int N);

    private static readonly Type[] Types =
    [
        typeof(int),
        typeof(long),
        typeof(string),
        typeof(Guid),
        typeof(DateTime),
        typeof(double),
    ];

    private static readonly Gen<(int Type, int Value)> AWrite = Gen.Select(
        Gen.Int[0, Types.Length - 1],
        Gen.Int[0, 3]
    );

    private static void Commutes(
        Func<
            IReadOnlyDictionary<Type, object>,
            IEnumerable<IReadOnlyDictionary<Type, object>>,
            Dictionary<Type, object>
        > merge
    ) =>
        Gen.Select(AWrite.Array[0, 4], AWrite.Array[0, 3].Array[1, 4])
            .SelectMany(t =>
                Gen.Shuffle(Enumerable.Range(0, t.Item2.Length).ToArray())
                    .Select(order => (Fork: t.Item1, Branches: t.Item2, Order: order))
            )
            .Sample(
                t =>
                {
                    var fork = new Dictionary<Type, object>();
                    foreach (var (type, value) in t.Fork)
                        fork[Types[type]] = new Box(value);

                    // Each branch starts from the fork's values, as a branch's Memory does, and writes
                    // its own: a type the fork had, or a new one, possibly one a sibling writes too.
                    var branches = t
                        .Branches.Select(writes =>
                        {
                            var memory = new Dictionary<Type, object>(fork);
                            foreach (var (type, value) in writes)
                                memory[Types[type]] = new Box(value);
                            return (IReadOnlyDictionary<Type, object>)memory;
                        })
                        .ToList();

                    var declared = merge(fork, branches);
                    var reordered = merge(fork, t.Order.Select(i => branches[i]));

                    reordered
                        .Should()
                        .BeEquivalentTo(
                            declared,
                            $"the join does not depend on branch order ({Adr})"
                        );
                },
                iter: 1000
            );

    #endregion

    #region Helpers

    /// <summary>
    /// A <see cref="ScheduledBranchRunner"/> that takes its choices at random from a seed, and
    /// remembers them, so a failure names the interleaving as well as the input.
    /// </summary>
    private sealed class Choices(int seed)
    {
        private readonly Random _random = new(seed);
        private readonly List<int> _taken = [];

        public ScheduledBranchRunner Runner() =>
            new(ready =>
            {
                var choice = _random.Next(ready.Count);
                lock (_taken)
                    _taken.Add(choice);
                return choice;
            });

        public override string ToString()
        {
            lock (_taken)
                return $"scheduled from seed {seed}, choices [{string.Join(", ", _taken)}]";
        }
    }

    /// <summary>
    /// Runs <paramref name="chain"/> on a <see cref="ScheduledBranchRunner"/> whose choices come
    /// from <paramref name="seed"/>. The run is deterministic, so the seed CsCheck prints replays
    /// the interleaving too, and <c>Replay</c> names the choices it took.
    /// </summary>
    private static async Task<(Outcome Outcome, RunLog Log, string Replay)> RunScheduled(
        IReadOnlyList<Element> chain,
        int seed
    )
    {
        var choices = new Choices(seed);
        var (outcome, log) = await Run(chain, choices.Runner());
        return (outcome, log, choices.ToString());
    }

    /// <summary>
    /// Checks a law again over runs on the thread pool: a smoke signal for an interleaving the
    /// scheduled runner cannot make, such as two junctions truly at once. No seed replays the
    /// thread pool's interleaving, so a failure here says so, rather than reading like one the
    /// printed seed reproduces.
    /// </summary>
    private static async Task OnThePool(Func<Task> law, string scheduledReplay)
    {
        try
        {
            await law();
        }
        catch (Exception e)
        {
            Assert.Fail(
                "Only the thread-pool run broke this law; the deterministic run of the same input "
                    + $"passed ({scheduledReplay}). The seed CsCheck prints replays the input, not "
                    + "the thread pool's interleaving, so it may not reproduce this failure: rerun "
                    + "it, or search the input's interleavings with Interleavings.Every.\n"
                    + e.Message
            );
        }
    }

    /// <summary>
    /// A concurrent run agrees with the in-order one on the result, Memory and, unless a sibling
    /// was stopped, what failed.
    /// </summary>
    private static void Agrees(
        IReadOnlyList<Element> chain,
        Outcome concurrent,
        Outcome inOrder,
        string run
    )
    {
        concurrent.IsRight.Should().Be(inOrder.IsRight, run);
        concurrent.Snapshot.Should().Be(inOrder.Snapshot, run);
        if (!CancelsSiblings(chain))
            concurrent.Leaves.Should().BeEquivalentTo(inOrder.Leaves, run);
    }

    private static async Task<(Outcome Outcome, RunLog Log)> Run(
        IReadOnlyList<Element> chain,
        IBranchRunner runner
    )
    {
        var log = new RunLog();
        var outcome = await Outcome
            .Of(new ShapeTrain(chain, log) { BranchRunner = runner }, "x")
            .WaitAsync(Hang);
        return (outcome, log);
    }

    /// <summary>Where a run breaks the laws its chain's description says it keeps.</summary>
    private static IEnumerable<string> Violations(
        IReadOnlyList<Element> chain,
        Outcome outcome,
        RunLog log
    )
    {
        var expected = Expected(chain, "x");

        if (outcome.IsRight)
        {
            if (expected.Faults.Count > 0)
                yield return $"succeeded, though junctions [{string.Join(", ", expected.Faults)}] fail";
            else if (outcome.Snapshot != expected.Snapshot)
                yield return $"ended with {outcome.Snapshot}, not {expected.Snapshot}";
            yield break;
        }

        if (outcome.Cancelled)
        {
            yield return $"was cancelled, though nothing cancelled it: {outcome}";
            yield break;
        }

        var failed = outcome
            .Leaves.Select(l => int.Parse(l.Message["fault ".Length..]))
            .ToHashSet();

        if (failed.Count == 0)
            yield return $"failed with nothing at the bottom of it: {outcome}";
        if (!failed.IsSubsetOf(expected.Faults))
            yield return $"reports failures [{string.Join(", ", failed)}], which cannot happen";
        if (!CancelsSiblings(chain) && !failed.SetEquals(expected.Faults))
            yield return $"reports [{string.Join(", ", failed.Order())}], not every failure "
                + $"[{string.Join(", ", expected.Faults.Order())}]";

        // Every failure that happened is carried, unless it happened inside a branch a sibling
        // stopped: that branch's failures are its own, and the branch is not a failure.
        var stopped = outcome.Stopped;
        var carried = log
            .Threw.Where(t => !stopped.Any(s => t.Branch == s || t.Branch.StartsWith(s + "/")))
            .ToHashSet();

        if (!outcome.Leaves.SetEquals(carried))
            yield return $"carries {outcome}, but what failed was "
                + $"[{string.Join(", ", carried.Select(c => $"{c.Branch}:{c.Message}").Order())}]";
    }

    /// <summary>Two runs that must agree but for node ids: on the result, Memory and what failed.</summary>
    private static void Same(IReadOnlyList<Element> chain, Outcome one, Outcome other, string law)
    {
        one.IsRight.Should().Be(other.IsRight, law);
        one.Snapshot.Should().Be(other.Snapshot, law);
        if (!CancelsSiblings(chain))
            one.Leaves.Select(l => l.Message)
                .Should()
                .BeEquivalentTo(other.Leaves.Select(l => l.Message), law);
    }

    private static IEnumerable<string> Ids(IEnumerable<ChainGraphNode> nodes) =>
        nodes.SelectMany(n => Ids(n.Tracks.SelectMany(t => t.Nodes)).Prepend(n.Id));

    #endregion
}
