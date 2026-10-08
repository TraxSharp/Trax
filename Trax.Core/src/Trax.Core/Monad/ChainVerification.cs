using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Extensions;
using Trax.Core.Functional;

namespace Trax.Core.Monad;

/// <summary>
/// One thing wrong with a declared chain.
/// </summary>
/// <param name="StepIndex">Position of the offending step, counting from zero.</param>
/// <param name="Kind">Which chain primitive declared the step.</param>
/// <param name="Junction">The junction the step names, or null for a step that names none.</param>
/// <param name="Reason">What is wrong, phrased for whoever has to fix it.</param>
/// <remarks>
/// A refusal about the chain as a whole, rather than one step, has a <see cref="StepIndex"/> one
/// past the last step, <see cref="ChainStepKind.Resolve"/> as its kind and no junction.
/// </remarks>
public readonly record struct ChainFault(
    int StepIndex,
    ChainStepKind Kind,
    Type? Junction,
    string Reason
)
{
    /// <summary>
    /// True when the fault is one of <see cref="ChainRecorder.Refusals"/>: something reading the
    /// chain refused, rather than a type the replay found missing. A refused step records no
    /// output, so later faults may be its consequences rather than faults of their own.
    /// </summary>
    public bool IsRefusal { get; init; }
}

/// <summary>
/// Replays a declared chain over the types Memory would hold, without running anything.
/// </summary>
/// <remarks>
/// Memory is keyed by type, so a chain either does or does not line up, and that is decidable
/// from the declaration alone. A junction whose input never reaches Memory fails at runtime only
/// on the path that reaches it; replayed here, it is a fact about the train that a host can check
/// for every train it has registered before it serves any traffic.
///
/// <para>The replay mirrors how the runtime stores and finds values, and the two differ by
/// source. The train's input enters Memory under its type and every interface it implements; a
/// junction's output, an extracted value and a service handed to <c>AddServices</c> enter under
/// exactly one type; a tuple contributes each element under its type and interfaces. Lookup is by
/// exact type, falling back to the container. A junction asking for an interface its producer's
/// declared output only implements is therefore a fault here because it fails at runtime.</para>
///
/// <para>What the replay cannot see is the concrete type of the train's input at runtime, only
/// the declared one. The run stores the input under both, so everything the replay counts as
/// available is; the reverse does not hold. A junction asking for an interface that only a
/// subtype of the declared input implements reads as a fault, although the run would find it.
/// Declaring the input as that subtype fixes both.</para>
/// </remarks>
public static class ChainVerification
{
    /// <summary>
    /// Replays <paramref name="chain"/> for a train taking <paramref name="input"/> and producing
    /// <paramref name="output"/>, returning everything that does not line up.
    /// </summary>
    /// <param name="chain">The steps the train declares.</param>
    /// <param name="input">The train's input type, which seeds Memory.</param>
    /// <param name="output">The train's return type, which the chain must end holding.</param>
    /// <param name="availableElsewhere">
    /// Answers whether a type the chain never produces can still be supplied, because a junction
    /// input not found in Memory falls back to the container. Without it, every junction taking
    /// an injected service reads as a fault.
    /// </param>
    public static IReadOnlyList<ChainFault> Verify(
        ChainRecorder chain,
        Type input,
        Type output,
        Func<Type, bool>? availableElsewhere = null
    ) => Verify(chain, input, output, availableElsewhere, checkConstructors: false);

    /// <summary>
    /// Replays <paramref name="chain"/> for a train run with a container, and also checks that
    /// every junction Trax builds from its constructor can be handed each argument.
    /// </summary>
    /// <remarks>
    /// A junction's constructor arguments are found the way its input is: in Memory as the chain
    /// has filled it by that step, then in the container. <paramref name="container"/> answers the
    /// second without building anything, so a service only a request can construct still counts
    /// as available. Junctions passed as instances or resolved by <c>IChain</c> are already built
    /// and are not checked.
    /// </remarks>
    /// <param name="chain">The steps the train declares.</param>
    /// <param name="input">The train's input type, which seeds Memory.</param>
    /// <param name="output">The train's return type, which the chain must end holding.</param>
    /// <param name="container">
    /// Answers whether the container the train runs with can supply a type.
    /// </param>
    public static IReadOnlyList<ChainFault> Verify(
        ChainRecorder chain,
        Type input,
        Type output,
        IServiceProviderIsService container
    )
    {
        ArgumentNullException.ThrowIfNull(container);

        return Verify(chain, input, output, container.IsService, checkConstructors: true);
    }

    private static IReadOnlyList<ChainFault> Verify(
        ChainRecorder chain,
        Type input,
        Type output,
        Func<Type, bool>? availableElsewhere,
        bool checkConstructors
    )
    {
        var memory = new System.Collections.Generic.HashSet<Type> { typeof(Unit) };

        // A train run with a container finds the container itself in Memory.
        if (checkConstructors)
            memory.Add(typeof(IServiceProvider));
        var faults = new List<ChainFault>();
        var steps = chain.Steps;

        Remember(memory, input, withInterfaces: true);

        if (TooManyTupleElements(input) is { } inputShape)
            faults.Add(
                new ChainFault(0, ChainStepKind.Seed, null, $"the train's input {inputShape}")
            );

        Replay(chain, memory, faults, output, availableElsewhere, checkConstructors, track: null);

        foreach (var refusal in chain.RecordedRefusals)
            faults.Add(
                new ChainFault(
                    refusal.StepIndex ?? steps.Count,
                    refusal.Kind ?? ChainStepKind.Resolve,
                    refusal.Junction,
                    refusal.Reason
                )
                {
                    IsRefusal = true,
                }
            );

        return faults;
    }

    /// <summary>
    /// Where a track being replayed sits in the train's chain: the outermost routing step's
    /// position and kind, and the words that name the track in a fault.
    /// </summary>
    private readonly record struct TrackContext(int SwitchIndex, ChainStepKind Kind, string Prefix);

    /// <summary>
    /// Replays <paramref name="chain"/>'s steps over <paramref name="memory"/>, which it leaves
    /// holding what the chain produced. A routing step replays each of its tracks from the Memory
    /// at that step and keeps only what every track produced, because the chain after it runs
    /// after whichever track was taken.
    /// </summary>
    private static void Replay(
        ChainRecorder chain,
        System.Collections.Generic.HashSet<Type> memory,
        List<ChainFault> faults,
        Type output,
        Func<Type, bool>? availableElsewhere,
        bool checkConstructors,
        TrackContext? track
    )
    {
        var steps = chain.Steps;

        // A fault inside a track is reported at the routing step, saying which track and step.
        ChainFault Fault(int i, ChainStepKind kind, Type? junction, string reason) =>
            track is { } t
                ? new ChainFault(t.SwitchIndex, t.Kind, junction, $"{t.Prefix}step {i}: {reason}")
                : new ChainFault(i, kind, junction, reason);

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (step.Kind == ChainStepKind.Parallel)
            {
                ReplayParallel(i, step);
                continue;
            }

            // A step naming a type that is not a junction is kept, without types, only so later
            // steps keep their written positions. Its refusal says what is wrong with it.
            if (step.Kind != ChainStepKind.Seed && step.In is null && step.Out is null)
                continue;

            switch (step.Kind)
            {
                case ChainStepKind.Resolve:
                    // A short circuit that returns Left lets the chain run on, so the rest of the
                    // chain still has to produce the return type for the path where it does.
                    if (!Satisfied(memory, output, availableElsewhere))
                        faults.Add(
                            Fault(
                                i,
                                step.Kind,
                                null,
                                $"the chain ends without '{Name(output)}' in Memory, so there is "
                                    + "nothing for Resolve to return. Chain a junction that "
                                    + "produces it."
                            )
                        );

                    continue;

                case ChainStepKind.Seed:
                    if (step.Out is { } seeded)
                        memory.Add(seeded);

                    continue;

                case ChainStepKind.Extract:
                    // Extract reads Memory only; unlike a junction input it never asks the
                    // container.
                    if (step.In is { } source && !memory.Contains(source))
                        faults.Add(
                            Fault(
                                i,
                                step.Kind,
                                null,
                                $"extracts from '{Name(source)}', which nothing before it puts in "
                                    + "Memory. Extract does not fall back to the container."
                            )
                        );

                    if (step.Out is { } extracted)
                        memory.Add(extracted);

                    continue;

                case ChainStepKind.Decide:
                    ReplayDecide(i, step);
                    continue;

                case ChainStepKind.Switch:
                case ChainStepKind.Gate:
                case ChainStepKind.Scale:
                    ReplayRouting(i, step);
                    continue;

                case ChainStepKind.IChain:
                    // IChain finds the junction itself in Memory or the container before it can
                    // ask the junction for its input.
                    if (
                        step.Junction is { } contract
                        && !Satisfied(memory, contract, availableElsewhere)
                    )
                        faults.Add(
                            Fault(
                                i,
                                step.Kind,
                                step.Junction,
                                $"resolves the junction '{Name(contract)}' from Memory or the "
                                    + "container and neither holds one. Register it, or use Chain "
                                    + "with the concrete junction type."
                            )
                        );

                    break;
            }

            // A short circuit's Right value becomes the train's result by a cast, which throws
            // unless its output can be the return type.
            if (
                step.Kind == ChainStepKind.ShortCircuit
                && step.Out is { } shortCircuitOut
                && !output.IsAssignableFrom(shortCircuitOut)
            )
                faults.Add(
                    Fault(
                        i,
                        step.Kind,
                        step.Junction,
                        $"short-circuits with '{Name(shortCircuitOut)}', which cannot be the "
                            + $"train's result '{Name(output)}'. A short circuit's output is "
                            + "returned as the result."
                    )
                );

            // The constructor runs before the input is extracted, against the same Memory.
            if (checkConstructors && chain.IsBuilt(i) && step.Junction is { } built)
                foreach (
                    var argument in UnsuppliedConstructorArguments(
                        built,
                        memory,
                        availableElsewhere
                    )
                )
                    faults.Add(
                        Fault(
                            i,
                            step.Kind,
                            step.Junction,
                            $"needs '{Name(argument)}' as a constructor argument"
                                + (
                                    argument.IsTuple()
                                        ? "; a tuple is assembled from Memory only, and nothing "
                                            + "before it puts every element there. Chain "
                                            + "junctions that produce them first."
                                        : "; nothing before it puts one in Memory and the "
                                            + "container does not register it. Register it or "
                                            + "chain a junction that produces it first."
                                )
                        )
                    );

            if (step.In is { } required && !Satisfied(memory, required, availableElsewhere))
                faults.Add(
                    Fault(
                        i,
                        step.Kind,
                        step.Junction,
                        $"needs '{Name(required)}' in Memory and nothing before it puts one "
                            + "there. Chain a junction that produces it first."
                    )
                );

            // A short circuit's output reaches Memory only when it returns Right, and on that
            // path the chain's result is already decided. The path that continues past it is
            // the one where it returned Left and stored nothing.
            if (step.Kind != ChainStepKind.ShortCircuit && step.Out is { } produced)
            {
                Remember(memory, produced, withInterfaces: false);

                if (TooManyTupleElements(produced) is { } producedShape)
                    faults.Add(
                        Fault(i, step.Kind, step.Junction, $"produces a value that {producedShape}")
                    );
            }
        }

        void ReplayDecide(int i, ChainStep step)
        {
            // A Decide records a step per question, each from the same state and decider. What was
            // missing at the step before is still missing, because a Decide adds only its
            // decision, so it is reported once, at the first question.
            var previous =
                i > 0 && steps[i - 1].Kind == ChainStepKind.Decide
                    ? steps[i - 1]
                    : (ChainStep?)null;

            if (
                step.In is { } state
                && previous?.In != state
                && !Satisfied(memory, state, availableElsewhere)
            )
                faults.Add(
                    Fault(
                        i,
                        step.Kind,
                        step.Junction,
                        $"decides from '{Name(state)}' and nothing before it puts one in Memory. "
                            + "Chain a junction that produces it first."
                    )
                );

            foreach (
                var decider in step.Junction is { } live && previous?.Junction != live
                    ? chain.RequirementsAt(i).Prepend(live)
                    : chain.RequirementsAt(i)
            )
                if (!Satisfied(memory, decider, availableElsewhere))
                    faults.Add(
                        Fault(
                            i,
                            step.Kind,
                            decider,
                            $"needs a decider '{Name(decider)}' and neither Memory nor the "
                                + "container holds one. Register it, or hand one to AddServices."
                        )
                    );

            if (step.Out is { } decision)
                memory.Add(decision);
        }

        void ReplayParallel(int i, ChainStep step)
        {
            // Every branch starts from Memory as it is here, and the step after the join sees the
            // union of what they added. A type two branches both add has no single value to merge,
            // so it is refused; an interface two tuples' elements both bring is left out of the
            // merge at run time, so it is left out here too.
            var fork = new System.Collections.Generic.HashSet<Type>(memory);
            var producedBy = new Dictionary<Type, string>();
            var collided = new System.Collections.Generic.HashSet<Type>();
            var reads = new List<(string Branch, Type Type)>();

            foreach (var declared in chain.TracksAt(i))
            {
                var branchMemory = new System.Collections.Generic.HashSet<Type>(fork);
                var context = new TrackContext(
                    track?.SwitchIndex ?? i,
                    track?.Kind ?? step.Kind,
                    $"{track?.Prefix}branch '{declared.Name}', "
                );

                Replay(
                    declared.Steps,
                    branchMemory,
                    faults,
                    output,
                    availableElsewhere,
                    checkConstructors,
                    context
                );

                foreach (var written in Outputs(declared.Steps).Where(fork.Contains).Where(Merged))
                    faults.Add(
                        Fault(
                            i,
                            step.Kind,
                            null,
                            $"branch '{declared.Name}' produces '{Name(written)}', which was in "
                                + "Memory before the Parallel. A branch adds to Memory; one that "
                                + "replaced a value would race its siblings reading it. Produce a "
                                + "new type."
                        )
                    );

                foreach (var input in Inputs(declared.Steps))
                    reads.Add((declared.Name, input));

                foreach (var added in branchMemory.Where(t => !fork.Contains(t) && Merged(t)))
                {
                    if (!producedBy.TryAdd(added, declared.Name))
                    {
                        if (added.IsInterface)
                            collided.Add(added);
                        else
                            faults.Add(
                                Fault(
                                    i,
                                    step.Kind,
                                    null,
                                    $"branches '{producedBy[added]}' and '{declared.Name}' both "
                                        + $"produce '{Name(added)}', so the join has two values "
                                        + "for one type. Have one branch produce it, or give each "
                                        + "its own type."
                                )
                            );
                    }
                }
            }

            // A branch cannot see what a sibling produces: they run at the same time. When the
            // container also supplies the type the branch would silently get the container's.
            foreach (var (branch, input) in reads)
                if (
                    !fork.Contains(input)
                    && producedBy.TryGetValue(input, out var sibling)
                    && sibling != branch
                )
                    faults.Add(
                        Fault(
                            i,
                            step.Kind,
                            null,
                            $"branch '{branch}' needs '{Name(input)}', which only branch "
                                + $"'{sibling}' produces. Branches run at the same time and see "
                                + "only what was in Memory before the Parallel. Produce it "
                                + "before the Parallel, or read it after the join."
                        )
                    );

            foreach (var added in producedBy.Keys)
                if (!collided.Contains(added))
                    memory.Add(added);
        }

        void ReplayRouting(int i, ChainStep step)
        {
            if (step.In is { } decision && !memory.Contains(decision))
                faults.Add(
                    Fault(
                        i,
                        step.Kind,
                        step.Junction,
                        $"routes on '{Name(decision)}' and nothing before it decides it. Ask it "
                            + "with Decide first, or use the form that asks its own question."
                    )
                );

            if (step.Out is { } choice)
                memory.Add(choice);

            System.Collections.Generic.HashSet<Type>? afterEveryTrack = null;

            foreach (var declared in chain.TracksAt(i))
            {
                var trackMemory = new System.Collections.Generic.HashSet<Type>(memory);
                var context = new TrackContext(
                    track?.SwitchIndex ?? i,
                    track?.Kind ?? step.Kind,
                    $"{track?.Prefix}track '{declared.Name}', "
                );

                Replay(
                    declared.Steps,
                    trackMemory,
                    faults,
                    output,
                    availableElsewhere,
                    checkConstructors,
                    context
                );

                if (afterEveryTrack is null)
                    afterEveryTrack = trackMemory;
                else
                    afterEveryTrack.IntersectWith(trackMemory);
            }

            // Every track starts from this Memory and only adds to it, so what every track holds
            // afterwards includes it.
            if (afterEveryTrack is not null)
                memory.UnionWith(afterEveryTrack);
        }
    }

    /// <summary>
    /// Whether a type a branch adds is merged into the run's Memory: <c>Unit</c> is always there,
    /// and what a branch decided and which track it took stay the branch's, so two branches can
    /// each route on the same question.
    /// </summary>
    internal static bool Merged(Type type) =>
        type != typeof(Unit)
        && !(
            type.IsGenericType
            && type.GetGenericTypeDefinition() is var open
            && (
                open == typeof(TrackTaken<>)
                || open == typeof(ChoiceDecision<>)
                || open == typeof(YesNoDecision<>)
                || open == typeof(ScoreDecision<>)
            )
        );

    /// <summary>Every type a chain's steps, and their tracks' and branches' steps, produce.</summary>
    private static IEnumerable<Type> Outputs(ChainRecorder chain) =>
        chain
            .Steps.Select((step, i) => (step, i))
            .SelectMany(s =>
                (
                    s.step.Kind == ChainStepKind.ShortCircuit || s.step.Out is null
                        ? []
                        : new[] { s.step.Out }
                ).Concat(chain.TracksAt(s.i).SelectMany(t => Outputs(t.Steps)))
            );

    /// <summary>Every type a chain's steps, and their tracks' and branches' steps, consume.</summary>
    private static IEnumerable<Type> Inputs(ChainRecorder chain) =>
        chain
            .Steps.Select((step, i) => (step, i))
            .SelectMany(s =>
                (s.step.In is null ? [] : new[] { s.step.In }).Concat(
                    chain.TracksAt(s.i).SelectMany(t => Inputs(t.Steps))
                )
            );

    /// <summary>
    /// The arguments of <paramref name="junction"/>'s one public constructor that neither Memory
    /// nor the container can supply. A junction Trax cannot build at all is refused when the
    /// chain is read, so it has nothing to report here.
    /// </summary>
    private static IEnumerable<Type> UnsuppliedConstructorArguments(
        Type junction,
        System.Collections.Generic.HashSet<Type> memory,
        Func<Type, bool>? availableElsewhere
    )
    {
        if (MonadExtensions.JunctionConstructorProblem(junction) is not null)
            return [];

        return junction
            .GetConstructors()[0]
            .GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => !Satisfied(memory, t, availableElsewhere))
            .Distinct();
    }

    /// <summary>
    /// Whether a junction can be handed something of this type.
    /// </summary>
    /// <remarks>
    /// Mirrors the ways the runtime finds one: a tuple is assembled from elements already in
    /// Memory, and anything else is taken from Memory by its exact type or, failing that, from
    /// the container. An <c>ILogger&lt;T&gt;</c> can also be made by an <c>ILoggerFactory</c>
    /// in Memory.
    /// </remarks>
    private static bool Satisfied(
        System.Collections.Generic.HashSet<Type> memory,
        Type required,
        Func<Type, bool>? availableElsewhere
    )
    {
        // A tuple is assembled from Memory alone; the runtime never asks the container for an
        // element, so neither does the replay.
        if (required.IsTuple())
            return required.GetGenericArguments().All(memory.Contains);

        return memory.Contains(required)
            || (availableElsewhere?.Invoke(required) ?? false)
            || (
                required.IsGenericType
                && required.GetGenericTypeDefinition() == typeof(ILogger<>)
                && memory.Contains(typeof(ILoggerFactory))
            );
    }

    /// <summary>
    /// Mirrors what a value entering Memory makes available. A tuple always contributes each
    /// element under its type and interfaces. Anything else contributes its own type, plus its
    /// interfaces only when it is the train's input.
    /// </summary>
    private static void Remember(
        System.Collections.Generic.HashSet<Type> memory,
        Type type,
        bool withInterfaces
    )
    {
        if (type.IsTuple())
        {
            // Deliberately not recursive. AddTupleToMemory stores each element under its own type
            // and stops: an element that is itself a tuple goes in as one value, and
            // ExtractTypeTuples does not take it apart again. Recursing here claimed the inner
            // elements were available, so a chain needing one passed the check and then failed at
            // runtime with the very "could not find type" this check exists to predict.
            foreach (var element in type.GetGenericArguments())
                Contribute(memory, element, withInterfaces: true);

            return;
        }

        Contribute(memory, type, withInterfaces);
    }

    /// <summary>
    /// Adds one value's own type, and its interfaces when they are findable, without looking
    /// inside it.
    /// </summary>
    private static void Contribute(
        System.Collections.Generic.HashSet<Type> memory,
        Type type,
        bool withInterfaces
    )
    {
        memory.Add(type);

        if (!withInterfaces)
            return;

        foreach (var contract in type.GetInterfaces())
            memory.Add(contract);
    }

    /// <summary>
    /// Why a tuple entering Memory cannot be stored, or null when it can.
    /// </summary>
    /// <remarks>
    /// <c>AddTupleToMemory</c> refuses a tuple longer than seven, and C# represents a longer one by
    /// nesting the remainder in an eighth generic argument, so that is what this looks for. Without
    /// the check the chain verified cleanly and threw the moment the value reached Memory, which
    /// inverts the whole point of verifying.
    /// </remarks>
    private static string? TooManyTupleElements(Type type) =>
        type.IsTuple() && type.GetGenericArguments().Length > 7
            ? $"'{Name(type)}' holds more than seven elements, which Memory cannot store. "
                + "Group the extra values into a type of their own."
            : null;

    private static string Name(Type type) => type.FullName ?? type.Name;
}
