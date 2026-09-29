namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// The page bounds every paged read on the operations surface applies to its <c>skip</c> and
/// <c>take</c> arguments, so one call never materialises a whole table.
/// </summary>
/// <remarks>
/// These match <c>Trax.Scheduler</c>'s <c>OperationsService.MaxPageSize</c> and its clamping:
/// <c>take</c> is clamped to 1 through <see cref="MaxPageSize"/> (so 0 or a negative value
/// returns one row), and a negative <c>skip</c> is treated as 0. The two must agree, because
/// the resolvers are moving onto that service.
/// </remarks>
internal static class OperationsPageBounds
{
    /// <summary>The largest page a paged operations read returns.</summary>
    public const int MaxPageSize = 500;

    /// <summary>Clamps a requested page size to 1 through <see cref="MaxPageSize"/>.</summary>
    public static int Take(int take) => Math.Clamp(take, 1, MaxPageSize);

    /// <summary>Treats a negative offset as 0.</summary>
    public static int Skip(int skip) => Math.Max(skip, 0);
}
