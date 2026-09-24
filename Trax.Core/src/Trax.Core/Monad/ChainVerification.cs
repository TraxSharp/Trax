using System.Runtime.CompilerServices;
using LanguageExt;
using Trax.Core.Extensions;

namespace Trax.Core.Monad;

/// <summary>
/// One thing wrong with a declared chain.
/// </summary>
/// <param name="StepIndex">Position of the offending step, counting from zero.</param>
/// <param name="Kind">Which chain primitive declared the step.</param>
/// <param name="Junction">The junction the step names, or null for a step that names none.</param>
/// <param name="Reason">What is wrong, phrased for whoever has to fix it.</param>
public readonly record struct ChainFault(
    int StepIndex,
    ChainStepKind Kind,
    Type? Junction,
    string Reason
);

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
    )
    {
        var memory = new System.Collections.Generic.HashSet<Type> { typeof(Unit) };
        var faults = new List<ChainFault>();
        var steps = chain.Steps;

        Remember(memory, input, withInterfaces: true);

        if (TooManyTupleElements(input) is { } inputShape)
            faults.Add(
                new ChainFault(0, ChainStepKind.Seed, null, $"the train's input {inputShape}")
            );

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            switch (step.Kind)
            {
                case ChainStepKind.Resolve:
                    // A short circuit that returns Left lets the chain run on, so the rest of the
                    // chain still has to produce the return type for the path where it does.
                    if (!Satisfied(memory, output, availableElsewhere))
                        faults.Add(
                            new ChainFault(
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
                            new ChainFault(
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

                case ChainStepKind.IChain:
                    // IChain finds the junction itself in Memory or the container before it can
                    // ask the junction for its input.
                    if (
                        step.Junction is { } contract
                        && !Satisfied(memory, contract, availableElsewhere)
                    )
                        faults.Add(
                            new ChainFault(
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
                    new ChainFault(
                        i,
                        step.Kind,
                        step.Junction,
                        $"short-circuits with '{Name(shortCircuitOut)}', which cannot be the "
                            + $"train's result '{Name(output)}'. A short circuit's output is "
                            + "returned as the result."
                    )
                );

            if (step.In is { } required && !Satisfied(memory, required, availableElsewhere))
                faults.Add(
                    new ChainFault(
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
                        new ChainFault(
                            i,
                            step.Kind,
                            step.Junction,
                            $"produces a value that {producedShape}"
                        )
                    );
            }
        }

        foreach (var refusal in chain.Refusals)
            faults.Add(new ChainFault(steps.Count, ChainStepKind.Resolve, null, refusal));

        return faults;
    }

    /// <summary>
    /// Whether a junction can be handed something of this type.
    /// </summary>
    /// <remarks>
    /// Mirrors the ways the runtime finds one: a tuple is assembled from elements already in
    /// Memory, and anything else is taken from Memory by its exact type or, failing that, from
    /// the container.
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

        return memory.Contains(required) || (availableElsewhere?.Invoke(required) ?? false);
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
