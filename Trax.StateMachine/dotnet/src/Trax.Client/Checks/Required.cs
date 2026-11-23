using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Trax.Client.Checks;

/// <summary>
/// Where the null-forgiving operator would have gone. Trax.Client takes no dependency on Trax.Core, so it carries
/// its own copy of Trax.Core's NotNull: the value, or an <see cref="InvalidOperationException"/> naming the
/// expression when it is null.
/// </summary>
internal static class Required
{
    [return: NotNull]
    internal static T NotNull<T>(
        [NotNull] this T? value,
        [CallerArgumentExpression(nameof(value))] string? valueExpr = null
    )
    {
        if (value is null)
            throw new InvalidOperationException($"{valueExpr} is null");
        return value;
    }
}
