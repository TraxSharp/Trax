namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// The page bounds every paged read on the operations surface applies to its <c>skip</c> and
/// <c>take</c> arguments, so one call never materialises a whole table.
/// </summary>
/// <remarks>
/// These match <c>Trax.Scheduler</c>'s <c>OperationsService.MaxPageSize</c> and its clamping:
/// <c>take</c> is clamped to 1 through <see cref="MaxPageSize"/> (so 0 or a negative value
/// returns one row), and a negative <c>skip</c> is treated as 0. The two must agree, because
/// the resolvers are moving onto that service. A <c>skip</c> above <see cref="MaxSkip"/> is
/// refused rather than clamped: an offset that deep makes the database read and discard every
/// skipped row, and serving a different page than the one asked for would be wrong, so the
/// caller is pointed at the <c>afterId</c> keyset cursor instead
/// (<c>docs/adr/0017-an-operations-page-is-at-most-500-rows.md</c>).
/// </remarks>
internal static class OperationsPageBounds
{
    /// <summary>The largest page a paged operations read returns.</summary>
    public const int MaxPageSize = 500;

    /// <summary>The deepest offset a paged operations read serves; deeper paging uses <c>afterId</c>.</summary>
    public const int MaxSkip = 10_000;

    /// <summary>The error code a <c>skip</c> above <see cref="MaxSkip"/> is refused with.</summary>
    public const string SkipTooDeepCode = "TRAX_SKIP_TOO_DEEP";

    /// <summary>Clamps a requested page size to 1 through <see cref="MaxPageSize"/>.</summary>
    public static int Take(int take) => Math.Clamp(take, 1, MaxPageSize);

    /// <summary>
    /// Treats a negative offset as 0, and refuses one above <see cref="MaxSkip"/> with a
    /// <see cref="SkipTooDeepCode"/> error that names the keyset cursor to page with instead.
    /// </summary>
    /// <exception cref="GraphQLException">The offset is above <see cref="MaxSkip"/>.</exception>
    public static int Skip(int skip)
    {
        if (skip > MaxSkip)
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        $"skip may be at most {MaxSkip}. To read further, page with afterId: "
                            + "pass each page's nextCursor as the next request's afterId."
                    )
                    .SetCode(SkipTooDeepCode)
                    .SetExtension("maxSkip", MaxSkip)
                    .Build()
            );

        return Math.Max(skip, 0);
    }
}
