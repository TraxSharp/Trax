using System.Linq.Expressions;
using System.Reflection;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Resolves a strongly-typed member expression (<c>x =&gt; x.PaidWith</c>) to the JSON field name it maps to
/// (<c>"paidWith"</c>). This is what lets the authoring surface reference context/input fields by member
/// instead of by string: refactor-safe and typo-proof, while the stored rule and the IR still carry the JSON
/// key as data.
/// </summary>
public static class MemberPath
{
    /// <summary>The JSON field name a single-member selector points at.</summary>
    public static string Of<T, TField>(Expression<Func<T, TField>> selector) =>
        ToJsonName(Member(selector).Name);

    private static MemberInfo Member<T, TField>(Expression<Func<T, TField>> selector)
    {
        // A selector over a value-typed property is wrapped in a Convert-to-object node; unwrap it.
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } u
            ? u.Operand
            : selector.Body;

        if (body is MemberExpression { Member: PropertyInfo or FieldInfo } m)
            return m.Member;

        throw new ArgumentException(
            $"Expected a single property/field selector like 'x => x.Field', got '{selector.Body}'.",
            nameof(selector)
        );
    }

    /// <summary>PascalCase C# member -> camelCase JSON key (the wire convention the existing machines use).</summary>
    public static string ToJsonName(string memberName) =>
        string.IsNullOrEmpty(memberName) || char.IsLower(memberName[0])
            ? memberName
            : char.ToLowerInvariant(memberName[0]) + memberName[1..];
}
