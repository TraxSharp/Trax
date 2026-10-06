using System.Linq.Expressions;
using System.Reflection;

namespace Trax.Api.GraphQL.Filtering.Operations;

/// <summary>
/// Expression-tree primitives shared by the case-insensitive string operation handlers.
/// </summary>
internal static class CaseInsensitiveExpression
{
    private static readonly MethodInfo ToLowerMethod = typeof(string).GetMethod(
        nameof(string.ToLower),
        Type.EmptyTypes
    )!;

    /// <summary>Wraps a string expression in <c>.ToLower()</c>.</summary>
    public static Expression Lower(Expression value) => Expression.Call(value, ToLowerMethod);

    /// <summary>Builds <c>instance != null</c> so the lowering call is null-safe.</summary>
    public static Expression NotNull(Expression instance) =>
        Expression.NotEqual(instance, Expression.Constant(null, typeof(string)));
}
