using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Resolves a strongly-typed member expression (<c>x =&gt; x.PaidWith</c>) to the JSON field name it maps to
/// (<c>"paidWith"</c>). This is what lets the authoring surface reference context/input fields by member
/// instead of by string: refactor-safe and typo-proof, while the stored rule and the IR still carry the JSON
/// key as data.
/// </summary>
internal static class MemberPath
{
    /// <summary>The JSON field name a single-member selector points at.</summary>
    public static string Of<T, TField>(Expression<Func<T, TField>> selector) =>
        JsonNameOf(Member(selector));

    /// <summary>
    /// The JSON key a member is written under: its <c>[JsonPropertyName]</c> when it has one, else its name in
    /// camelCase. Rules, reductions and the reflected schema all name a field this way, so they agree with each
    /// other and with what System.Text.Json writes for the record.
    /// </summary>
    public static string JsonNameOf(MemberInfo member) =>
        member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? ToJsonName(member.Name);

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

    /// <summary>
    /// A C# member name as <see cref="JsonNamingPolicy.CamelCase"/> writes it (<c>PaidWith</c> -> <c>paidWith</c>,
    /// <c>ID</c> -> <c>id</c>), the policy the server serializes a machine's records with, so the name a client
    /// sends is the name the server binds.
    /// </summary>
    public static string ToJsonName(string memberName) =>
        string.IsNullOrEmpty(memberName)
            ? memberName
            : JsonNamingPolicy.CamelCase.ConvertName(memberName);
}
