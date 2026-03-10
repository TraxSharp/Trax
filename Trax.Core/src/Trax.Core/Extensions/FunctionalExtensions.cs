using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using LanguageExt;

namespace Trax.Core.Extensions;

/// <summary>
/// Provides extension methods for functional programming patterns.
/// These methods enhance the Railway-oriented programming model used throughout Trax.Core.
/// </summary>
public static class FunctionalExtensions
{
    private static readonly ConcurrentDictionary<Type, bool> TupleCache = new();

    /// <summary>
    /// Unwraps a Task of Either, returning the Right value or throwing the Left exception.
    /// This is used to convert from the Railway pattern back to traditional exception handling.
    /// </summary>
    /// <typeparam name="L">The Left type (must be an Exception)</typeparam>
    /// <typeparam name="R">The Right type (the result type)</typeparam>
    /// <param name="option">The Task of Either to unwrap</param>
    /// <returns>The Right value if present</returns>
    /// <exception cref="L">Thrown if the Either contains a Left value</exception>
    /// <remarks>
    /// This method is typically used at the boundary of a Railway-oriented system,
    /// where you need to convert back to traditional exception handling.
    /// </remarks>
    internal static async Task<R> Unwrap<L, R>(this Task<Either<L, R>> option)
        where L : Exception
    {
        var result = await option;
        if (result.IsRight)
            return result.RightToSeq().Head();
        else
            throw result.LeftToSeq().Head;
    }

    /// <summary>
    /// Unwraps an Either, returning the Right value or throwing the Left exception.
    /// This is used to convert from the Railway pattern back to traditional exception handling.
    /// </summary>
    /// <typeparam name="L">The Left type (must be an Exception)</typeparam>
    /// <typeparam name="R">The Right type (the result type)</typeparam>
    /// <param name="option">The Either to unwrap</param>
    /// <returns>The Right value if present</returns>
    /// <exception cref="L">Thrown if the Either contains a Left value</exception>
    /// <remarks>
    /// This method is typically used at the boundary of a Railway-oriented system,
    /// where you need to convert back to traditional exception handling.
    /// </remarks>
    internal static R Unwrap<L, R>(this Either<L, R> option)
        where L : Exception
    {
        if (option.IsRight)
            return option.RightToSeq().Head();

        throw option.LeftToSeq().Head;
    }

    /// <summary>
    /// Determines whether a type is a tuple type.
    /// </summary>
    /// <param name="type">The type to check</param>
    /// <returns>True if the type is a tuple type, false otherwise</returns>
    /// <remarks>
    /// This method checks for both ValueTuple and ITuple types.
    /// It's used throughout Trax.Core to handle tuple types specially in Memory.
    /// </remarks>
    internal static bool IsTuple(this Type type) =>
        TupleCache.GetOrAdd(
            type,
            t =>
                (t.IsGenericType && t.FullName?.StartsWith("System.ValueTuple`") == true)
                || t.FullName?.StartsWith("System.Runtime.CompilerServices.ITuple") == true
        );

    /// <summary>
    /// Asserts that a nullable value is not null, throwing <see cref="InvalidOperationException"/>
    /// with the caller's expression if null. Prefer over the null-forgiving operator (!) for runtime validation.
    /// </summary>
    /// <typeparam name="T">The type of the value</typeparam>
    /// <param name="value">The value to check</param>
    /// <param name="valueExpr">Auto-captured caller argument expression</param>
    /// <exception cref="InvalidOperationException">Thrown when the value is null</exception>
    public static void AssertLoaded<T>(
        [NotNull] this T? value,
        [CallerArgumentExpression("value")] string? valueExpr = null
    )
    {
        if (value == null)
            throw new InvalidOperationException($"{valueExpr} has not been loaded");
    }

    /// <summary>
    /// Asserts that a selector applied to each element of the collection produces a non-null value.
    /// Use for validating that navigation properties are loaded on a collection of entities.
    /// </summary>
    /// <typeparam name="T">The element type of the collection</typeparam>
    /// <typeparam name="U">The type returned by the selector</typeparam>
    /// <param name="values">The collection to validate</param>
    /// <param name="selector">The selector to apply to each element</param>
    /// <param name="valuesExpr">Auto-captured caller argument expression for the collection</param>
    /// <param name="selectorExpr">Auto-captured caller argument expression for the selector</param>
    /// <exception cref="InvalidOperationException">Thrown when any selected value is null</exception>
    public static void AssertEachLoaded<T, U>(
        [NotNull] this IEnumerable<T> values,
        Func<T, U> selector,
        [CallerArgumentExpression("values")] string? valuesExpr = null,
        [CallerArgumentExpression("selector")] string? selectorExpr = null
    )
    {
        foreach (var value in values)
        {
            if (selector(value) == null)
                throw new InvalidOperationException(
                    $"{valuesExpr}({selectorExpr}) has not been loaded"
                );
        }
    }
}
