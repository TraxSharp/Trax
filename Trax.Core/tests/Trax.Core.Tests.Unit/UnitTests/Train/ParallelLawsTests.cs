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
/// <para>A failure prints the seed CsCheck shrank it to. Pin it as its own test with
/// <c>Sample(..., seed: "...")</c> before fixing the code, so the case stays covered.</para>
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
                    var pool = await Run(chain, ThreadPoolBranchRunner.Instance);
                    var inOrder = await Run(chain, new SequentialBranchRunner());
                    var random = new Random(seed);
                    var scheduled = await Run(
                        chain,
                        new ScheduledBranchRunner(ready => random.Next(ready.Count))
                    );

                    foreach (var (outcome, log) in new[] { pool, inOrder, scheduled })
                        Violations(chain, outcome, log)
                            .Should()
                            .BeEmpty($"every run keeps the laws of Parallel ({Adr})");

                    foreach (var (concurrent, _) in new[] { pool, scheduled })
                    {
                        concurrent.IsRight.Should().Be(inOrder.Outcome.IsRight);
                        concurrent.Snapshot.Should().Be(inOrder.Outcome.Snapshot);
                        if (!CancelsSiblings(chain))
                            concurrent.Leaves.Should().BeEquivalentTo(inOrder.Outcome.Leaves);
                    }
                },
                iter: 1000,
                print: t => $"scheduled from {t.Item2}: {Describe(t.Item1)}"
            );

    [Test]
    public Task RunEither_NeverThrows_AndGivesOneResult_WhereverTheRunIsCancelled() =>
        AShape()
            .SelectMany(chain => Gen.Int[0, Steps(chain)].Select(at => (chain, at)))
            .SampleAsync(
                async Task (t) =>
                {
                    var (chain, at) = t;
                    using var cts = new CancellationTokenSource();
                    var log = new RunLog { CancelAt = (at, cts) };
                    var train = new ShapeTrain(chain, log) { CancellationToken = cts.Token };

                    var outcome = await Outcome.Of(train, "x").WaitAsync(Hang);

                    if (!outcome.Cancelled)
                        Violations(chain, outcome, log)
                            .Should()
                            .BeEmpty(
                                $"a run is cancelled, or keeps the laws it keeps uncancelled ({Adr})"
                            );
                },
                iter: 1000,
                print: t => $"cancel at {t.at}: {Describe(t.chain)}"
            );

    #endregion

    #region Shape

    [Test]
    public Task AParallelOfOneBranch_IsThatBranchsChain() =>
        AChain(1, 4, 8)
            .SampleAsync(
                async Task (IReadOnlyList<Element> body) =>
                {
                    var alone = Number(body);
                    var wrapped = Number([
                        new Fork([new Branch("", body)], BranchFailurePolicy.WaitForAll),
                    ]);

                    var plain = await Run(alone, ThreadPoolBranchRunner.Instance);
                    var parallel = await Run(wrapped, ThreadPoolBranchRunner.Instance);

                    Same(alone, plain.Outcome, parallel.Outcome, "Parallel(a) is a");
                },
                iter: 500,
                print: body => Describe(Number(body))
            );

    [Test]
    public Task ANestedParallel_FlattensIntoItsParent() =>
        Gen.Select(AChain(1, 3, 8), AChain(1, 3, 8), AChain(1, 3, 8))
            .SampleAsync(
                async Task (
                    IReadOnlyList<Element> a,
                    IReadOnlyList<Element> b,
                    IReadOnlyList<Element> c
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

                    var one = await Run(nested, ThreadPoolBranchRunner.Instance);
                    var other = await Run(flat, ThreadPoolBranchRunner.Instance);

                    Same(
                        flat,
                        one.Outcome,
                        other.Outcome,
                        "Parallel(a, Parallel(b, c)) is Parallel(a, b, c), but for node ids"
                    );
                },
                iter: 500,
                print: t =>
                    $"a: {Describe(Number(t.Item1))}; b: {Describe(Number(t.Item2))}; c: {Describe(Number(t.Item3))}"
            );

    [Test]
    public Task TheOrderBranchesAreDeclaredIn_DoesNotChangeTheResult() =>
        AFork(2, 8)
            .Select(fork => Number([fork]))
            .SelectMany(chain =>
                Gen.Shuffle(((Fork)chain[0]).Branches.ToArray())
                    .Select(shuffled => (chain, shuffled))
            )
            .SampleAsync(
                async Task (t) =>
                {
                    var (chain, shuffled) = t;
                    var reordered =
                        (IReadOnlyList<Element>)[((Fork)chain[0]) with { Branches = shuffled }];

                    var declared = await Run(chain, ThreadPoolBranchRunner.Instance);
                    var swapped = await Run(reordered, ThreadPoolBranchRunner.Instance);

                    Same(chain, declared.Outcome, swapped.Outcome, "the join commutes");
                },
                iter: 500,
                print: t => Describe(t.chain)
            );

    [Test]
    public Task GraphIds_StayUnique_AndEveryJunctionThatRunsReportsOneOfThem() =>
        AShape()
            .SampleAsync(
                async Task (IReadOnlyList<Element> chain) =>
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

                    var (_, log) = await Run(chain, ThreadPoolBranchRunner.Instance);
                    foreach (var (_, node, branch) in log.Ran)
                    {
                        ids.Should().Contain(node!, "a running junction says which node it is");
                        if (branch is not null)
                            node.Should().StartWith(branch + "/", "a branch's steps are inside it");
                    }
                },
                iter: 500,
                print: Describe
            );

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
