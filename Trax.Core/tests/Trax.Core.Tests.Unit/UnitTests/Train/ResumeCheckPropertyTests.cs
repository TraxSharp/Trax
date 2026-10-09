using System.Collections.Concurrent;
using System.Reflection;
using AwesomeAssertions;
using CsCheck;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;
using static Trax.Core.Tests.Unit.UnitTests.Train.CheckpointFixtures;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// The resume check against the run it predicts, over generated chains: where it allows a
/// resume, the resumed run finishes, and after the latest checkpoint gives what a run gives that
/// ran exactly the steps the resumed run ran; where it refuses for a missing input, running from
/// the point anyway fails. A refusal for a stale value is the check being stricter than the run:
/// the run would finish, reading an older value.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
/// <remarks>
/// A chain is a sequence of maps between five value types, each reading one type already in
/// Memory and writing another, with checkpoints of types already in Memory between them, at most
/// one <c>Parallel</c> whose two branches map into types of their own and may checkpoint them,
/// ending in a map to the result type. A map overwriting a type is what makes a resume point
/// matter: the value a step reads may come from before the checkpoint, from the checkpoint, or
/// from a step after the point.
///
/// <para>Each map's output depends on the round it runs in. The original run is round 1; a
/// resumed run is round 2, so a step it runs again gives a different value than it gave before.
/// What it should give is what a full run gives in which exactly those steps are round 2: a
/// restored value is the round-1 value, and anything computed from a step that ran again is a
/// round-2 one. A restored state computed from a value the resumed run changed shows up as a
/// difference.</para>
/// </remarks>
[Property("adr", CheckpointFixtures.Adr)]
public class ResumeCheckPropertyTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private const int A1 = 5;
    private const int A2 = 6;
    private const int B1 = 7;

    private static readonly Type[] Values =
    [
        typeof(V0),
        typeof(V1),
        typeof(V2),
        typeof(V3),
        typeof(V4),
        typeof(BranchA1),
        typeof(BranchA2),
        typeof(BranchB1),
    ];

    [Test]
    public Task The_check_agrees_with_running_from_the_point_over_generated_chains() =>
        AChain.SampleAsync(
            async ops =>
            {
                var store = new Store();
                var full = await new GeneratedTrain(
                    ops,
                    new Services().With<ICheckpointStore>(store),
                    Rounds.All(1)
                ).RunEither(new V0(1));
                full.IsRight.Should().BeTrue("a generated chain is valid by construction");

                var written = store.Taken.Select(t => t.NodeId).ToList();
                var chain = new GeneratedTrain(ops, new Services(), Rounds.All(1)).DeclaredChain();
                var points = ChainVerification.NodeOrder(chain).Where(Startable).Prepend(null);

                foreach (var point in points)
                {
                    var outcome = ChainVerification.CheckResume(
                        chain,
                        typeof(V0),
                        typeof(Out),
                        t => t.IsInterface,
                        written,
                        point
                    );

                    if (outcome.CanResume)
                    {
                        var rounds = Rounds.All(2);
                        var resumed = await Resumed(ops, Plan(outcome, store), rounds);
                        resumed
                            .IsRight.Should()
                            .BeTrue(
                                $"the check allowed resuming at {point ?? "the latest checkpoint"}, "
                                    + $"so no step from there lacks an input ({Adr})"
                            );

                        // After the latest checkpoint every value a later step reads is the
                        // checkpoint's or recomputed from it, as in a full run in which the
                        // same steps ran again. An operator's later point also skips the steps
                        // between, which is what they asked.
                        if (point is null)
                        {
                            var expected = await new GeneratedTrain(
                                ops,
                                new Services(),
                                rounds.RanAgain()
                            ).RunEither(new V0(1));

                            resumed
                                .ValueUnsafe()
                                .Should()
                                .Be(
                                    expected.ValueUnsafe(),
                                    "a resumed run equals a full run in which the steps it ran "
                                        + $"again ran again ({Adr})"
                                );
                        }
                    }
                    else if (
                        outcome.RefusalCode == ResumeRefusals.MissingInput
                        && Forced(chain, store, written, point) is { } forcedPlan
                    )
                    {
                        var forced = await Resumed(ops, forcedPlan, Rounds.All(2));
                        forced
                            .IsLeft.Should()
                            .BeTrue(
                                $"the check refused {point ?? "the latest checkpoint"} for a missing input, "
                                    + $"so running from it fails: {outcome.Refusal} ({Adr})"
                            );
                    }
                }
            },
            iter: 400,
            print: ops => string.Join(" ", ops)
        );

    private static bool Startable(string? id) =>
        id is not null && !id.StartsWith("Seed<", StringComparison.Ordinal) && id != "Resolve#0";

    private static Task<Either<Exception, Out>> Resumed(Op[] ops, ResumePlan plan, Rounds rounds) =>
        new GeneratedTrain(ops, new Services().With<ICheckpointStore>(new Store()), rounds)
        {
            Resume = plan,
        }.RunEither(new V0(1));

    private static ResumePlan Plan(ResumeOutcome outcome, Store store)
    {
        var restored = new Dictionary<string, RestoredCheckpoint>();
        if (outcome.MainCheckpoint is { } main)
            restored[""] = store.Restored(main);
        foreach (var (branch, node) in outcome.BranchCheckpoints)
            restored[branch] = store.Restored(node);

        return new ResumePlan(outcome.Target, outcome.Inclusive, restored);
    }

    /// <summary>
    /// The plan the check would have made, ignoring its own refusal, or null when that depends on
    /// which branch checkpoints it would have kept: an operator's point at the <c>Parallel</c>
    /// restores every branch's latest checkpoint; any other plan restores none.
    /// </summary>
    private static ResumePlan? Forced(
        ChainRecorder chain,
        Store store,
        IReadOnlyList<string> written,
        string? point
    )
    {
        var order = ChainVerification.NodeOrder(chain).ToList();
        var mainWritten = written.Where(w => !w.Contains('/')).ToList();
        var limit = point is null ? order.Count : order.IndexOf(point);
        var main = mainWritten.Where(w => order.IndexOf(w) < limit).MaxBy(order.IndexOf);
        var restored = new Dictionary<string, RestoredCheckpoint>();
        if (main is not null)
            restored[""] = store.Restored(main);

        if (point is null)
            return written.Any(w => w.Contains('/')) ? null : new ResumePlan(main, true, restored);

        foreach (
            var branch in written
                .Where(w => w.StartsWith(point + "/", StringComparison.Ordinal))
                .GroupBy(w => w[..w.LastIndexOf('/')])
        )
            restored[branch.Key] = store.Restored(branch.MaxBy(order.IndexOf)!);

        return new ResumePlan(point, false, restored);
    }

    public enum OpKind
    {
        Map,
        Checkpoint,
        Parallel,
    }

    /// <summary>
    /// A map from one value type to another, a checkpoint of one, or a <c>Parallel</c> whose
    /// branches read <see cref="From"/>: branch a maps it to A1, may checkpoint it, and may map
    /// that to A2; branch b maps it to B1 and may checkpoint it.
    /// </summary>
    public readonly record struct Op(
        OpKind Kind,
        int From,
        int To,
        bool ACheckpoint = false,
        bool ASecond = false,
        bool BCheckpoint = false
    )
    {
        public override string ToString() =>
            Kind switch
            {
                OpKind.Checkpoint => $"Checkpoint<{Name(From)}>",
                OpKind.Parallel => $"Parallel({Name(From)}->A1"
                    + (ACheckpoint ? " Checkpoint<A1>" : "")
                    + (ASecond ? " A1->A2" : "")
                    + $" | {Name(From)}->B1"
                    + (BCheckpoint ? " Checkpoint<B1>" : "")
                    + ")",
                _ => $"{Name(From)}->{(To < 0 ? "Out" : Name(To))}",
            };

        private static string Name(int index) =>
            index switch
            {
                A1 => "A1",
                A2 => "A2",
                B1 => "B1",
                _ => $"V{index}",
            };
    }

    /// <summary>
    /// Valid by construction: every op reads a type already in Memory, and only the one
    /// <c>Parallel</c> produces the branch types, so the chain verifies and a full run succeeds.
    /// </summary>
    private static readonly Gen<Op[]> AChain = Gen.Int[0, int.MaxValue]
        .Array[2, 9]
        .Select(choices =>
        {
            var available = new List<int> { 0 };
            var ops = new List<Op>();
            var forked = false;

            foreach (var choice in choices[..^1])
            {
                var from = available[choice % available.Count];

                if (choice % 3 == 0)
                    ops.Add(new Op(OpKind.Checkpoint, from, from));
                else if (choice % 5 == 1 && !forked)
                {
                    forked = true;
                    var op = new Op(
                        OpKind.Parallel,
                        from,
                        -1,
                        ACheckpoint: (choice / 11) % 2 == 0,
                        ASecond: (choice / 13) % 2 == 0,
                        BCheckpoint: (choice / 17) % 2 == 0
                    );
                    ops.Add(op);
                    available.Add(A1);
                    available.Add(B1);
                    if (op.ASecond)
                        available.Add(A2);
                }
                else
                {
                    var to = (choice / 7) % 5;
                    ops.Add(new Op(OpKind.Map, from, to));
                    if (!available.Contains(to))
                        available.Add(to);
                }
            }

            ops.Add(new Op(OpKind.Map, available[choices[^1] % available.Count], -1));
            return ops.ToArray();
        });

    public interface IValue
    {
        int Value { get; }
    }

    public sealed record V0(int Value) : IValue;

    public sealed record V1(int Value) : IValue;

    public sealed record V2(int Value) : IValue;

    public sealed record V3(int Value) : IValue;

    public sealed record V4(int Value) : IValue;

    public sealed record BranchA1(int Value) : IValue;

    public sealed record BranchA2(int Value) : IValue;

    public sealed record BranchB1(int Value) : IValue;

    public sealed record Out(int Value) : IValue;

    /// <summary>
    /// Which round each map runs in, by its step number, and which steps ran: the original run is
    /// round 1, a resumed run round 2.
    /// </summary>
    public sealed class Rounds(Func<int, int> round)
    {
        private readonly ConcurrentDictionary<int, byte> _ran = new();

        public static Rounds All(int round) => new(_ => round);

        public int Run(int step)
        {
            _ran.TryAdd(step, 0);
            return round(step);
        }

        /// <summary>A full run's rounds in which the steps this run ran are round 2, the rest round 1.</summary>
        public Rounds RanAgain()
        {
            var ran = _ran.Keys.ToHashSet();
            return new Rounds(step => ran.Contains(step) ? 2 : 1);
        }
    }

    /// <summary>Writes <typeparamref name="TTo"/> from <typeparamref name="TFrom"/>, so the value says which steps ran, and in which round.</summary>
    private sealed class Map<TFrom, TTo>(int step, Rounds rounds) : Junction<TFrom, TTo>
        where TFrom : IValue
    {
        public override Task<TTo> Run(TFrom input) =>
            Task.FromResult(
                (TTo)
                    Activator.CreateInstance(
                        typeof(TTo),
                        input.Value * 31 + step + 1000 * rounds.Run(step)
                    )!
            );
    }

    private sealed class GeneratedTrain(Op[] ops, Services services, Rounds rounds) : Train<V0, Out>
    {
        private static readonly MethodInfo MapStep = typeof(GeneratedTrain).GetMethod(
            nameof(MapOf),
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        private static readonly MethodInfo CheckpointStep = typeof(GeneratedTrain).GetMethod(
            nameof(CheckpointOf),
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        protected override Task<Either<Exception, Out>> Junctions()
        {
            var chain = new MonadTask<V0, Out>(
                Task.FromResult(AddServices<IServiceProvider>(services))
            );

            for (var i = 0; i < ops.Length; i++)
            {
                var op = ops[i];
                var step = i * 4;

                chain = op.Kind switch
                {
                    OpKind.Checkpoint => Checkpoint(chain, op.From),
                    OpKind.Parallel => chain.Parallel(p =>
                        p.Branch(
                                "a",
                                b =>
                                {
                                    b = Map(b, op.From, A1, step + 1);
                                    if (op.ACheckpoint)
                                        b = Checkpoint(b, A1);
                                    return op.ASecond ? Map(b, A1, A2, step + 2) : b;
                                }
                            )
                            .Branch(
                                "b",
                                b =>
                                {
                                    b = Map(b, op.From, B1, step + 3);
                                    return op.BCheckpoint ? Checkpoint(b, B1) : b;
                                }
                            )
                    ),
                    _ => Map(chain, op.From, op.To, step),
                };
            }

            return chain.Resolve();
        }

        private MonadTask<V0, Out> Map(MonadTask<V0, Out> chain, int from, int to, int step) =>
            (MonadTask<V0, Out>)
                MapStep
                    .MakeGenericMethod(Values[from], to < 0 ? typeof(Out) : Values[to])
                    .Invoke(null, [chain, step, rounds])!;

        private static MonadTask<V0, Out> Checkpoint(MonadTask<V0, Out> chain, int state) =>
            (MonadTask<V0, Out>)
                CheckpointStep.MakeGenericMethod(Values[state]).Invoke(null, [chain])!;

        private static MonadTask<V0, Out> MapOf<TFrom, TTo>(
            MonadTask<V0, Out> chain,
            int step,
            Rounds rounds
        )
            where TFrom : IValue => chain.Chain(new Map<TFrom, TTo>(step, rounds));

        private static MonadTask<V0, Out> CheckpointOf<TState>(MonadTask<V0, Out> chain) =>
            chain.Checkpoint<TState>();
    }
}
