using System.Linq.Expressions;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// The ergonomic, string-free authoring surface over the <see cref="Rule"/> / <see cref="Reduction"/> data.
/// Import it with <c>using static Trax.Effect.StateMachine.Rules;</c> and reference fields by member
/// expression, so a guard reads <c>Input((CoinInput i) =&gt; i.Coin).IsOneOf("quarter", "dollar")</c> instead
/// of a raw record with a magic string. These are thin factories: they only build the data the engine and the
/// IR exporter already handle.
/// </summary>
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
}

/// <summary>A field to build a guard/validator rule over. Terminal methods return the <see cref="Rule"/>.</summary>
public sealed class FieldMatcher(RuleSource source, string field)
{
    /// <summary>The field is present and not null.</summary>
    public Rule Present() => new Rule.Present(source, field);

    /// <summary>The field is missing or null.</summary>
    public Rule Absent() => new Rule.Absent(source, field);

    /// <summary>A non-empty string or array.</summary>
    public Rule NonEmpty() => new Rule.NonEmpty(source, field);

    /// <summary>A string field whose value is one of a fixed set.</summary>
    public Rule IsOneOf(params string[] values) => new Rule.OneOf(source, field, values);

    /// <summary>The field is present and of the given JSON type.</summary>
    public Rule OfType(JsonFieldType type) => new Rule.OfType(source, field, type);

    /// <summary>A numeric field greater than a constant.</summary>
    public Rule GreaterThan(double value) =>
        new Rule.Compare(source, field, CompareOp.GreaterThan, value);

    /// <summary>A numeric field equal to a constant.</summary>
    public Rule EqualTo(double value) => new Rule.Compare(source, field, CompareOp.EqualTo, value);

    /// <summary>An array field with more than <paramref name="value"/> elements.</summary>
    public Rule CountGreaterThan(int value) =>
        new Rule.Count(source, field, CompareOp.GreaterThan, value);

    /// <summary>An array field with at least <paramref name="value"/> elements.</summary>
    public Rule CountAtLeast(int value) =>
        new Rule.Count(source, field, CompareOp.GreaterOrEqual, value);
}

/// <summary>Completes a <see cref="Rules.Set{TContext,TField}"/>: where the field's new value comes from.</summary>
public sealed class SetBuilder(string field)
{
    /// <summary>Set the field from a trigger-input field.</summary>
    public Reduction FromInput<TInput, TField>(Expression<Func<TInput, TField>> input) =>
        new Reduction.Set([new SetStep(field, new ValueSource.FromInput(MemberPath.Of(input)))]);

    /// <summary>Set the field to a literal value.</summary>
    public Reduction ToValue(JsonNode? value) =>
        new Reduction.Set([new SetStep(field, new ValueSource.Constant(value))]);
}
