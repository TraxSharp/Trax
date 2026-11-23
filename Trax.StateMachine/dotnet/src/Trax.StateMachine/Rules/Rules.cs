using System.Linq.Expressions;
using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// The ergonomic, string-free authoring surface over the <see cref="Rule"/> / <see cref="Reduction"/> data.
/// Import it with <c>using static Trax.StateMachine.Rules;</c> and reference fields by member
/// expression, so a guard reads <c>Input((CoinInput i) =&gt; i.Coin).IsOneOf("quarter", "dollar")</c> instead
/// of a raw record with a magic string. These are thin factories: they only build the data the engine and the
/// IR exporter already handle.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/rules">SDK reference</seealso>
public static class Rules
{
    /// <summary>Match a field on the trigger input.</summary>
    public static FieldMatcher Input<TInput, TField>(Expression<Func<TInput, TField>> selector) =>
        new(RuleSource.Input, MemberPath.Of(selector));

    /// <summary>Match a field on the snapshot context.</summary>
    public static FieldMatcher Field<TContext, TField>(
        Expression<Func<TContext, TField>> selector
    ) => new(RuleSource.Context, MemberPath.Of(selector));

    /// <summary>All sub-rules hold (logical AND).</summary>
    public static Rule All(params Rule[] rules) => new Rule.All(rules);

    /// <summary>At least one sub-rule holds (logical OR).</summary>
    public static Rule Any(params Rule[] rules) => new Rule.Any(rules);

    /// <summary>Produce an empty context.</summary>
    public static Reduction Clear() => new Reduction.Clear();

    /// <summary>Produce the machine's initial context.</summary>
    public static Reduction Reset() => new Reduction.Reset();

    /// <summary>Carry the current context forward unchanged.</summary>
    public static Reduction Keep() => new Reduction.Keep();

    /// <summary>Clone the context and set a field (complete with <c>.FromInput(...)</c> or <c>.ToValue(...)</c>).</summary>
    public static SetBuilder Set<TContext, TField>(Expression<Func<TContext, TField>> field) =>
        new(MemberPath.Of(field));

    /// <summary>
    /// Clone the context and set several fields in one reduction, each from its own <see cref="Set{TContext,TField}"/>:
    /// <c>SetAll(Set(c =&gt; c.A).FromInput(...), Set(c =&gt; c.B).ToValue(...))</c>. A transition or outcome edge
    /// takes one reduction, so this is how it fills several fields. The steps are applied in order.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A reduction is not a <see cref="Reduction.Set"/>, or two of them set the same field.
    /// </exception>
    public static Reduction SetAll(params Reduction[] sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var steps = new List<SetStep>();
        foreach (var reduction in sets)
        {
            if (reduction is not Reduction.Set set)
                throw new ArgumentException(
                    $"SetAll combines Set reductions; {reduction?.GetType().Name ?? "null"} is not one.",
                    nameof(sets)
                );
            steps.AddRange(set.Steps);
        }

        if (
            steps
                .GroupBy(s => s.Field, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Skip(1).Any()) is
            { } twice
        )
            throw new ArgumentException(
                $"SetAll sets '{twice.Key}' more than once; set each field once.",
                nameof(sets)
            );
        return new Reduction.Set(steps);
    }
}
