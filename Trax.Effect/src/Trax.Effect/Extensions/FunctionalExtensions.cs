using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.StepEffectRunner;

namespace Trax.Effect.Extensions;

public static class FunctionalExtensions
{
    public static void AssertLoaded<T>(
        [NotNull] this T? value,
        [CallerArgumentExpression("value")] string? valueExpr = null
    )
    {
        if (value == null)
        {
            if (
                value is EffectRunner
                || value is IEffectRunner
                || value is StepEffectRunner
                || value is IStepEffectRunner
            )
                throw new InvalidOperationException(
                    $"{valueExpr} has not been loaded. Ensure services.AddTraxEffects() is being added to your Dependency Injection Container"
                );

            if (value is IServiceProvider)
                throw new InvalidOperationException(
                    $"{valueExpr} has not been loaded. Ensure IServiceProvider is being added to your Dependency Injection Container"
                );

            throw new InvalidOperationException($"{valueExpr} has not been loaded");
        }
    }

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
