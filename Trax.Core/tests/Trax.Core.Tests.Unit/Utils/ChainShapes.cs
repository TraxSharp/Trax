using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using CsCheck;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.Utils;

/// <summary>
/// Trains built from a description of their chain, so a property test can generate chains rather
/// than write them: linear runs of junctions, <c>Parallel</c> steps with any number of branches,
/// nested <c>Parallel</c>s, <c>Parallel</c> inside a <c>Switch</c> track and a <c>Decide</c> and
/// <c>Switch</c> inside a branch, with any junction able to fail.
/// </summary>
/// <remarks>
/// <para>Every junction produces its own type, numbered in the order the description is read,
/// and reads the value the junction before it in the same chain produced (the seed for the first
/// junction of a branch or track), so the chain is one the startup check accepts and every value
/// in Memory says which junctions made it.</para>
/// <para>Because each junction's type and value are fixed by the description,
/// <see cref="Expected"/> can say what a run must end with without running it: the denotational
/// oracle. Every run, concurrent or not, is checked against it.</para>
/// </remarks>
internal static class ChainShapes
{
    #region The description

    internal abstract record Element;

    /// <summary>A junction: <see cref="Id"/> is the type it produces, <see cref="From"/> the one it reads.</summary>
    internal sealed record Link(bool Fails, int Id = -1, int From = -1) : Element;

    internal sealed record Branch(string Name, IReadOnlyList<Element> Body);

    internal sealed record Fork(IReadOnlyList<Branch> Branches, BranchFailurePolicy Policy)
        : Element;

    /// <summary>A <c>Decide</c> and a <c>Switch</c> whose chosen track is always Left.</summary>
    internal sealed record Route(bool OnLeft, IReadOnlyList<Element> Body) : Element;

    [Asks("Which lane?")]
    public enum Lane
    {
        Left,
        Right,
    }

    /// <summary>
    /// Numbers a description's junctions, in reading order from 1 (the seed is 0), and names its
    /// branches. Two descriptions that read their junctions in the same order number them the same.
    /// </summary>
    public static IReadOnlyList<Element> Number(IReadOnlyList<Element> chain)
    {
        var (next, names) = (1, 0);
        return Walk(chain, 0);

        IReadOnlyList<Element> Walk(IReadOnlyList<Element> body, int cursor)
        {
            var numbered = new List<Element>();
            foreach (var element in body)
            {
                switch (element)
                {
                    case Link step:
                        numbered.Add(step with { Id = next, From = cursor });
                        cursor = next++;
                        break;
                    case Fork fork:
                        numbered.Add(
                            fork with
                            {
                                Branches = fork
                                    .Branches.Select(b => new Branch(
                                        b.Name is ""
                                            ? string.Create(
                                                CultureInfo.InvariantCulture,
                                                $"b{names++}"
                                            )
                                            : b.Name,
                                        Walk(b.Body, cursor)
                                    ))
                                    .ToList(),
                            }
                        );
                        break;
                    case Route route:
                        numbered.Add(route with { Body = Walk(route.Body, cursor) });
                        break;
                }
            }

            return numbered;
        }
    }

    public static int Steps(IReadOnlyList<Element> chain) =>
        chain.Sum(e =>
            e switch
            {
                Link => 1,
                Fork f => f.Branches.Sum(b => Steps(b.Body)),
                Route r => Steps(r.Body),
                _ => 0,
            }
        );

    public static bool CancelsSiblings(IReadOnlyList<Element> chain) =>
        chain.Any(e =>
            e switch
            {
                Fork f => f.Policy == BranchFailurePolicy.CancelSiblings
                    || f.Branches.Any(b => CancelsSiblings(b.Body)),
                Route r => CancelsSiblings(r.Body),
                _ => false,
            }
        );

    public static string Describe(IReadOnlyList<Element> chain) =>
        string.Join(
            ' ',
            chain.Select(e =>
                e switch
                {
                    Link s => s.Fails
                        ? string.Create(CultureInfo.InvariantCulture, $"!{s.Id}")
                        : string.Create(CultureInfo.InvariantCulture, $"{s.Id}"),
                    Fork f => $"Parallel[{f.Policy}]("
                        + string.Join(
                            " | ",
                            f.Branches.Select(b => $"{b.Name}: {Describe(b.Body)}")
                        )
                        + ")",
                    Route r => $"Switch({(r.OnLeft ? "taken" : "not taken")}: {Describe(r.Body)})",
                    _ => "?",
                }
            )
        );

    #endregion

    #region Generators

    /// <summary>A junction that fails about one time in <paramref name="faultOneIn"/>.</summary>
    public static Gen<Element> AStep(int faultOneIn) =>
        Gen.Int[1, faultOneIn].Select(n => (Element)new Link(n == 1));

    /// <summary>A chain of one to <paramref name="longest"/> elements, nested up to <paramref name="depth"/> deep.</summary>
    public static Gen<IReadOnlyList<Element>> AChain(int depth, int longest, int faultOneIn) =>
        AnElement(depth, faultOneIn).List[1, longest].Select(l => (IReadOnlyList<Element>)l);

    private static Gen<Element> AnElement(int depth, int faultOneIn) =>
        depth == 0
            ? AStep(faultOneIn)
            : Gen.Frequency(
                (4, AStep(faultOneIn)),
                (2, AFork(depth, faultOneIn)),
                (1, ARoute(depth, faultOneIn))
            );

    public static Gen<Element> AFork(int depth, int faultOneIn) =>
        Gen.Select(
            AChain(depth - 1, 3, faultOneIn).List[1, 3],
            Gen.Enum<BranchFailurePolicy>(),
            (bodies, policy) =>
                (Element)new Fork(bodies.Select(b => new Branch("", b)).ToList(), policy)
        );

    private static Gen<Element> ARoute(int depth, int faultOneIn) =>
        Gen.Select(
            Gen.Int[1, 4].Select(n => n > 1),
            AChain(depth - 1, 2, faultOneIn),
            (onLeft, body) => (Element)new Route(onLeft, body)
        );

    /// <summary>A numbered chain small enough to run hundreds of times.</summary>
    public static Gen<IReadOnlyList<Element>> AShape(int depth = 2, int faultOneIn = 12) =>
        AChain(depth, 4, faultOneIn).Where(c => Steps(c) <= 60).Select(Number);

    #endregion

    #region The oracle

    /// <summary>What a run of <paramref name="chain"/> on <paramref name="input"/> must end with.</summary>
    /// <param name="Faults">
    /// The failing junctions that run when every branch runs to its own end: every failure a
    /// <see cref="BranchFailurePolicy.WaitForAll"/> run reports. Empty when the run succeeds.
    /// </param>
    /// <param name="Snapshot">The junction outputs in Memory at the end of a run that succeeds.</param>
    internal sealed record Expectation(IReadOnlySet<int> Faults, string Snapshot);

    public static Expectation Expected(IReadOnlyList<Element> chain, string input)
    {
        var faults = new HashSet<int>();
        var values = new SortedDictionary<int, string> { [0] = input };
        Walk(chain);
        return new Expectation(faults, Snapshot(values));

        bool Walk(IReadOnlyList<Element> body)
        {
            foreach (var element in body)
            {
                switch (element)
                {
                    case Link step when step.Fails:
                        faults.Add(step.Id);
                        return true;
                    case Link step:
                        values[step.Id] = Value(values[step.From], step.Id);
                        break;
                    case Fork fork:
                        // Every branch runs, whichever fails: a WaitForAll run.
                        var failed = false;
                        foreach (var branch in fork.Branches)
                            failed |= Walk(branch.Body);
                        if (failed)
                            return true;
                        break;
                    case Route route when route.OnLeft:
                        if (Walk(route.Body))
                            return true;
                        break;
                }
            }

            return false;
        }
    }

    public static string Value(string from, int id) =>
        string.Create(CultureInfo.InvariantCulture, $"{from}>{id}");

    public static string Fault(int id) =>
        string.Create(CultureInfo.InvariantCulture, $"fault {id}");

    private static string Snapshot(IEnumerable<KeyValuePair<int, string>> values) =>
        string.Join(
            ';',
            values.Select(v => string.Create(CultureInfo.InvariantCulture, $"{v.Key}={v.Value}"))
        );

    #endregion

    #region Running

    /// <summary>What a junction saw and did in one run.</summary>
    internal sealed class RunLog
    {
        public ConcurrentQueue<(int Id, string? Node, string? Branch)> Ran { get; } = new();

        public ConcurrentQueue<(string Branch, string Message)> Threw { get; } = new();

        /// <summary>A junction to cancel the run from, by its id, and the run's source.</summary>
        public (int Id, CancellationTokenSource Source)? CancelAt { get; init; }
    }

    public interface ISlot
    {
        int Id { get; }
        string Value { get; }
    }

    // Six markers make 216 distinct slot types, more than a generated chain is allowed to use.
    public sealed class D0;

    public static class D1;

    public static class D2;

    public static class D3;

    public static class D4;

    public static class D5;

    public sealed record Slot<TA, TB, TC>(int Id, string Value) : ISlot;

    private static readonly Type[] Markers =
    [
        typeof(D0),
        typeof(D1),
        typeof(D2),
        typeof(D3),
        typeof(D4),
        typeof(D5),
    ];

    public static Type SlotType(int id) =>
        typeof(Slot<,,>).MakeGenericType(
            Markers[id % 6],
            Markers[id / 6 % 6],
            Markers[id / 36 % 6]
        );

    internal sealed class Seed : Junction<string, Slot<D0, D0, D0>>
    {
        public override Task<Slot<D0, D0, D0>> Run(string input) =>
            Task.FromResult(new Slot<D0, D0, D0>(0, input));
    }

    internal sealed class Make<TIn, TOut>(Link step, RunLog run) : Junction<TIn, TOut>
        where TIn : ISlot
        where TOut : ISlot
    {
        public override async Task<TOut> Run(TIn input)
        {
            var branch = ChainGraph.CurrentBranchPath;
            run.Ran.Enqueue((step.Id, ChainGraph.CurrentNodeId, branch));

            if (run.CancelAt is { } cancel && cancel.Id == step.Id)
                await cancel.Source.CancelAsync().ConfigureAwait(false);

            if (step.Fails)
            {
                run.Threw.Enqueue((branch ?? "", Fault(step.Id)));
                throw new InvalidOperationException(Fault(step.Id));
            }

            return (TOut)
                Activator
                    .CreateInstance(typeof(TOut), step.Id, Value(input.Value, step.Id))
                    .NotNull();
        }
    }

    private static readonly MethodInfo ChainOne = typeof(ChainShapes)
        .GetMethod(nameof(ChainJunction), BindingFlags.NonPublic | BindingFlags.Static)
        .NotNull();

    private static MonadTask<string, string> ChainJunction<TIn, TOut>(
        MonadTask<string, string> chain,
        Link step,
        RunLog run
    )
        where TIn : ISlot
        where TOut : ISlot => chain.Chain(new Make<TIn, TOut>(step, run));

    public static MonadTask<string, string> Build(
        MonadTask<string, string> chain,
        IReadOnlyList<Element> body,
        RunLog run
    ) => body.Aggregate(chain, (so, element) => Add(so, element, run));

    private static MonadTask<string, string> Add(
        MonadTask<string, string> chain,
        Element element,
        RunLog run
    ) =>
        element switch
        {
            Link step => (MonadTask<string, string>)
                ChainOne
                    .MakeGenericMethod(SlotType(step.From), SlotType(step.Id))
                    .Invoke(null, [chain, step, run])
                    .NotNull(),
            Fork fork => chain.Parallel(p =>
                fork.Branches.Aggregate(
                        p,
                        (declared, branch) =>
                            declared.Branch(branch.Name, b => Build(b, branch.Body, run))
                    )
                    .OnFailure(fork.Policy)
            ),
            Route route => chain
                .Decide<string>(q => q.Choice<Lane>())
                .Switch<Lane>(t =>
                    t.When(Lane.Left, l => route.OnLeft ? Build(l, route.Body, run) : l)
                        .When(Lane.Right, r => route.OnLeft ? r : Build(r, route.Body, run))
                ),
            _ => throw new ArgumentOutOfRangeException(nameof(element)),
        };

    /// <summary>
    /// A train whose chain is <see cref="Shape"/>: an <see cref="IDecider"/> that always chooses
    /// Left, the seed, then the described elements. It keeps the Memory its run ended with.
    /// </summary>
    internal sealed class ShapeTrain(IReadOnlyList<Element> chain, RunLog run)
        : Train<string, string>
    {
        public IReadOnlyList<Element> Shape { get; } = chain;

        /// <summary>The junction outputs in Memory when the run ended without failing.</summary>
        public string? Snapshot { get; private set; }

        protected override Task<Either<Exception, string>> Junctions() =>
            IsDeclaringChain ? Build(Start(), Shape, run).Resolve() : RunAndKeep();

        private MonadTask<string, string> Start() =>
            AddServices<IDecider>(new ScriptedDecider().Choose(Lane.Left)).Chain(new Seed());

        private async Task<Either<Exception, string>> RunAndKeep()
        {
            var monad = await Build(Start(), Shape, run).ConfigureAwait(false);

            if (monad.Exception is null)
                Snapshot = ChainShapes.Snapshot(
                    monad
                        .Memory.Values.OfType<ISlot>()
                        .OrderBy(s => s.Id)
                        .Select(s => KeyValuePair.Create(s.Id, s.Value))
                );

            return monad.Resolve();
        }
    }

    #endregion
}
