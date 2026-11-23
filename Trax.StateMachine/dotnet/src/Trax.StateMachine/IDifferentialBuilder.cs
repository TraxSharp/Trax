using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// Authors a machine's differential fuzzing inputs. Each call adds one representative input. The harness
/// always fires a no-input case per trigger, so an explicit <see cref="EmptySample"/> (<c>{}</c>) is a
/// distinct case. Typed overloads serialize the record with camelCase names (nulls kept), matching the field
/// names the guards and reducers read; raw <see cref="JsonObject"/> overloads give exact control.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine#idifferentialbuilder">SDK reference</seealso>
public interface IDifferentialBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>A representative input for <paramref name="trigger"/>, from a typed record.</summary>
    IDifferentialBuilder<TState, TTrigger> Sample<TInput>(TTrigger trigger, TInput input);

    /// <summary>A representative input for <paramref name="trigger"/>, as a raw JSON object.</summary>
    IDifferentialBuilder<TState, TTrigger> Sample(TTrigger trigger, JsonObject input);

    /// <summary>An empty (<c>{}</c>) input for <paramref name="trigger"/> — distinct from the no-input case.</summary>
    IDifferentialBuilder<TState, TTrigger> EmptySample(TTrigger trigger);

    /// <summary>A seed context for <paramref name="state"/> (a BFS start point that reaches states the initial snapshot can't), from a typed record. A state may be seeded several times, and the corpus expands every seed; the same context twice for one state is refused at Build.</summary>
    IDifferentialBuilder<TState, TTrigger> Seed<TContext>(TState state, TContext context);

    /// <summary>A seed context for <paramref name="state"/>, as a raw JSON object. A state may be seeded several times, and the corpus expands every seed; the same context twice for one state is refused at Build.</summary>
    IDifferentialBuilder<TState, TTrigger> Seed(TState state, JsonObject context);

    /// <summary>A dense probe context crossed with EVERY state (exercises guards/validators on unreachable-but-sendable snapshots), from a typed record.</summary>
    IDifferentialBuilder<TState, TTrigger> Probe<TContext>(TContext context);

    /// <summary>A dense probe context, as a raw JSON object.</summary>
    IDifferentialBuilder<TState, TTrigger> Probe(JsonObject context);

    /// <summary>
    /// A representative output of the train <paramref name="invokingState"/> invokes, fired as that state's
    /// <c>done</c> outcome trigger (<c>&lt;State&gt;.done</c>), from a typed record. The harness also fires every
    /// outcome trigger with no input.
    /// </summary>
    IDifferentialBuilder<TState, TTrigger> OutcomeSample<TOutput>(
        TState invokingState,
        TOutput output
    ) => throw new NotSupportedException($"{GetType().Name} does not support outcome samples.");
}
