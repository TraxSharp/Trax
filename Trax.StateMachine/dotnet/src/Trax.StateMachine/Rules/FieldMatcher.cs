using System.Linq.Expressions;
using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// A field to build a guard/validator rule over. Terminal methods return the <see cref="Rule"/>. A comparison's bound
/// must be finite: <see cref="double.NaN"/> or an infinity throws <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
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
        new Rule.Compare(source, field, CompareOp.GreaterThan, Finite(value, nameof(GreaterThan)));

    /// <summary>A numeric field less than a constant (<c>lt</c> in the IR).</summary>
    public Rule LessThan(double value) =>
        new Rule.Compare(source, field, CompareOp.LessThan, Finite(value, nameof(LessThan)));

    /// <summary>A numeric field greater than or equal to a constant (<c>gte</c> in the IR).</summary>
    public Rule AtLeast(double value) =>
        new Rule.Compare(source, field, CompareOp.GreaterOrEqual, Finite(value, nameof(AtLeast)));

    /// <summary>A numeric field less than or equal to a constant (<c>lte</c> in the IR).</summary>
    public Rule AtMost(double value) =>
        new Rule.Compare(source, field, CompareOp.LessOrEqual, Finite(value, nameof(AtMost)));

    /// <summary>A numeric field equal to a constant.</summary>
    public Rule EqualTo(double value) =>
        new Rule.Compare(source, field, CompareOp.EqualTo, Finite(value, nameof(EqualTo)));

    // A NaN bound makes every comparison false, and an infinite one cannot be written to the IR, whose JSON has no
    // number for it; both are refused here, where the rule is written, rather than later.
    private double Finite(double value, string method) =>
        double.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"{method}({value.ToString(System.Globalization.CultureInfo.InvariantCulture)}) on '{field}' "
                    + "needs a finite bound. NaN makes every comparison false, and the IR has no number for an infinity."
            );

    /// <summary>An array field with more than <paramref name="value"/> elements.</summary>
    public Rule CountGreaterThan(int value) =>
        new Rule.Count(source, field, CompareOp.GreaterThan, value);

    /// <summary>An array field with at least <paramref name="value"/> elements.</summary>
    public Rule CountAtLeast(int value) =>
        new Rule.Count(source, field, CompareOp.GreaterOrEqual, value);

    /// <summary>A string field whose length is greater than a constant.</summary>
    public Rule LengthGreaterThan(int value) =>
        new Rule.Length(source, field, CompareOp.GreaterThan, value);

    /// <summary>A string field whose length is at least a constant.</summary>
    public Rule LengthAtLeast(int value) =>
        new Rule.Length(source, field, CompareOp.GreaterOrEqual, value);

    /// <summary>A boolean field equal to <c>true</c>.</summary>
    public Rule IsTrue() => new Rule.BoolEquals(source, field, Value: true);

    /// <summary>A boolean field equal to <c>false</c>.</summary>
    public Rule IsFalse() => new Rule.BoolEquals(source, field, Value: false);

    /// <summary>An array field whose every element is of the given JSON type.</summary>
    public Rule ArrayOf(JsonFieldType type) => new Rule.ArrayOf(source, field, type);
}
