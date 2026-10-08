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
/// resume, the resumed run finishes, and after the latest checkpoint gives what a full run gives;
/// where it refuses for a missing input, running from the point anyway fails. A refusal for a
/// stale value is the check being stricter than the run: the run would finish, reading an older
/// value.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
/// <remarks>
/// A chain is a sequence of maps between six value types, each reading one type already in
/// Memory and writing another, with checkpoints of types already in Memory between them, ending
/// in a map to the result type. A map overwriting a type is what makes a resume point matter:
/// the value a step reads may come from before the checkpoint, from the checkpoint, or from a
/// step after the point.
/// </remarks>
[Property("adr", CheckpointFixtures.Adr)]
public class ResumeCheckPropertyTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private static readonly Type[] Values =
    [
        typeof(V0),
        typeof(V1),
        typeof(V2),
        typeof(V3),
        typeof(V4),
    ];

    [Test]
    public Task The_check_agrees_with_running_from_the_point_over_generated_chains() =>
        AChain.SampleAsync(
            async ops =>
            {
                var store = new Store();
                var full = await new GeneratedTrain(
                    ops,
                    new Services().With<ICheckpointStore>(store)
                ).RunEither(new V0(1));
                full.IsRight.Should().BeTrue("a generated chain is valid by construction");

                var written = store.Taken.Select(t => t.NodeId).ToList();
                var chain = new GeneratedTrain(ops, new Services()).DeclaredChain();
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
                        var resumed = await Resumed(ops, store, Plan(outcome, store));
                        resumed
                            .IsRight.Should()
                            .BeTrue(
                                $"the check allowed resuming at {point ?? "the latest checkpoint"}, "
                                    + $"so no step from there lacks an input ({Adr})"
                            );

                        // After the latest checkpoint every value a later step reads is the
                        // checkpoint's or recomputed from it, as in the full run. An operator's
                        // later point also skips the steps between, which is what they asked.
                        if (point is null)
                            resumed
                                .ValueUnsafe()
                                .Should()
                                .Be(full.ValueUnsafe(), $"a resumed run equals a full run ({Adr})");
                    }
                    else if (outcome.RefusalCode == ResumeRefusals.MissingInput)
                    {
                        var forced = await Resumed(
                            ops,
                            store,
                            Forced(chain, store, written, point)
                        );
                        forced
                            .IsLeft.Should()
                            .BeTrue(
                                $"the check refused {point ?? "the latest checkpoint"} for a missing input, "
                                    + $"so running from it fails: {outcome.Refusal} ({Adr})"
                            );
                    }
                }
            },
            iter: 300,
            print: ops => string.Join(" ", ops)
        );

    private static bool Startable(string? id) =>
        id is not null && !id.StartsWith("Seed<", StringComparison.Ordinal) && id != "Resolve#0";

    private static Task<Either<Exception, Out>> Resumed(Op[] ops, Store store, ResumePlan plan) =>
        new GeneratedTrain(ops, new Services().With<ICheckpointStore>(new Store()))
        {
            Resume = plan,
        }.RunEither(new V0(1));

    private static ResumePlan Plan(ResumeOutcome outcome, Store store) =>
        new(
            outcome.Target,
            outcome.Inclusive,
            outcome.MainCheckpoint is { } main
                ? new Dictionary<string, RestoredCheckpoint> { [""] = store.Restored(main) }
                : []
        );

    /// <summary>The plan the check would have made, ignoring its own refusal.</summary>
    private static ResumePlan Forced(
        ChainRecorder chain,
        Store store,
        IReadOnlyList<string> written,
        string? point
    )
    {
        var order = ChainVerification.NodeOrder(chain).ToList();
        var limit = point is null ? order.Count : order.IndexOf(point);
        var main = written.Where(w => order.IndexOf(w) < limit).MaxBy(order.IndexOf);
        var restored = main is null
            ? []
            : new Dictionary<string, RestoredCheckpoint> { [""] = store.Restored(main) };

        return point is null
            ? new ResumePlan(main, true, restored)
            : new ResumePlan(point, false, restored);
    }

    /// <summary>A map from one value type to another, or a checkpoint of one.</summary>
    public readonly record struct Op(bool IsCheckpoint, int From, int To)
    {
        public override string ToString() =>
            IsCheckpoint ? $"Checkpoint<V{From}>" : $"V{From}->V{To}";
    }

    /// <summary>
    /// Valid by construction: every op reads a type already in Memory, so the chain verifies and a
    /// full run succeeds.
    /// </summary>
    private static readonly Gen<Op[]> AChain = Gen.Int[0, int.MaxValue]
        .Array[2, 9]
        .Select(choices =>
        {
            var available = new List<int> { 0 };
            var ops = new List<Op>();

            foreach (var choice in choices[..^1])
            {
                var from = available[choice % available.Count];

                if (choice % 3 == 0)
                    ops.Add(new Op(true, from, from));
                else
                {
                    var to = (choice / 7) % Values.Length;
                    ops.Add(new Op(false, from, to));
                    if (!available.Contains(to))
                        available.Add(to);
                }
            }

            ops.Add(new Op(false, available[choices[^1] % available.Count], -1));
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

    public sealed record Out(int Value) : IValue;

    /// <summary>Writes <typeparamref name="TTo"/> from <typeparamref name="TFrom"/>, so the value says which steps ran.</summary>
    private sealed class Map<TFrom, TTo>(int salt) : Junction<TFrom, TTo>
        where TFrom : IValue
    {
        public override Task<TTo> Run(TFrom input) =>
            Task.FromResult((TTo)Activator.CreateInstance(typeof(TTo), input.Value * 31 + salt)!);
    }

    private sealed class GeneratedTrain(Op[] ops, Services services) : Train<V0, Out>
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
                var from = Values[op.From];
                chain = op.IsCheckpoint
                    ? (MonadTask<V0, Out>)
                        CheckpointStep.MakeGenericMethod(from).Invoke(null, [chain])!
                    : (MonadTask<V0, Out>)
                        MapStep
                            .MakeGenericMethod(from, op.To < 0 ? typeof(Out) : Values[op.To])
                            .Invoke(null, [chain, i])!;
            }

            return chain.Resolve();
        }

        private static MonadTask<V0, Out> MapOf<TFrom, TTo>(MonadTask<V0, Out> chain, int salt)
            where TFrom : IValue => chain.Chain(new Map<TFrom, TTo>(salt));

        private static MonadTask<V0, Out> CheckpointOf<TState>(MonadTask<V0, Out> chain) =>
            chain.Checkpoint<TState>();
    }
}
