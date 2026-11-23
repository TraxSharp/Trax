using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

/// <summary>
/// Fluent surface for how <c>[TraxQueryModel]</c> lists are paged.
/// </summary>
public partial class TraxGraphQLBuilder
{
    /// <summary>The largest <c>skip</c> an offset-paged field accepts unless the host changes it.</summary>
    public const int DefaultMaxOffsetSkip = 10_000;

    internal TraxPaging DefaultPagingValue { get; private set; } = TraxPaging.Cursor;

    internal int MaxOffsetSkipValue { get; private set; } = DefaultMaxOffsetSkip;

    /// <summary>
    /// Sets the paging style of every query model whose <c>[TraxQueryModel]</c> leaves
    /// <see cref="TraxQueryModelAttribute.Paging"/> at <see cref="TraxPaging.Default"/>. The default
    /// is <see cref="TraxPaging.Cursor"/>. A model that names a style keeps it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="paging"/> is <see cref="TraxPaging.Default"/>, or not a defined value.
    /// </exception>
    public TraxGraphQLBuilder DefaultPaging(TraxPaging paging)
    {
        if (paging == TraxPaging.Default || !Enum.IsDefined(paging))
            throw new ArgumentOutOfRangeException(
                nameof(paging),
                paging,
                "DefaultPaging takes None, Cursor or Offset."
            );
        DefaultPagingValue = paging;
        return this;
    }

    /// <summary>
    /// Sets the largest <c>skip</c> an offset-paged query model accepts. The default is
    /// <see cref="DefaultMaxOffsetSkip"/>. A larger <c>skip</c> is refused with
    /// <c>TRAX_OFFSET_SKIP_LIMIT</c> before the query reaches the database, because the database
    /// reads and discards every skipped row.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSkip"/> is negative.</exception>
    public TraxGraphQLBuilder MaxOffsetSkip(int maxSkip)
    {
        if (maxSkip < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxSkip),
                maxSkip,
                "MaxOffsetSkip must not be negative."
            );
        MaxOffsetSkipValue = maxSkip;
        return this;
    }

    private TraxPaging ResolvePaging(TraxQueryModelAttribute attribute) =>
        attribute.Paging == TraxPaging.Default ? DefaultPagingValue : attribute.Paging;
}
