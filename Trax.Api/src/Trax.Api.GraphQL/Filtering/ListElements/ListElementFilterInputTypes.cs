using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>
/// Filter input types used for the <i>elements</i> of a scalar collection property.
/// They mirror HotChocolate's stock operation filter inputs with <c>neq</c> removed.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate exposes the same operation filter input for a scalar property and for
/// the elements of a collection property. That is a problem for one operation: inside a
/// collection, <c>neq</c> lowers to <c>Any(x =&gt; x != value)</c> over a primitive
/// collection, which no EF Core provider can translate. The query passes GraphQL
/// validation and then throws at execution:
/// </para>
/// <code>
/// The LINQ expression .Where(p => EF.Property&lt;Badge[]&gt;(p, "Badges")
///     .AsQueryable().Any(p0 => p0 != @p)) could not be translated.
/// </code>
/// <para>
/// Removing <c>neq</c> from the shared scalar input would also remove it from ordinary
/// scalar fields, where it translates perfectly well. So the element position gets its
/// own set of types instead, bound by property runtime type (see
/// <see cref="ListElementFilterBinding"/>). Every other operation is untouched:
/// <c>eq</c>, <c>in</c>, <c>nin</c> and the comparable operators all translate.
/// </para>
/// <para>
/// The one filter this costs is <c>all: { neq: X }</c>, which did translate.
/// <c>none: { eq: X }</c> is exactly equivalent and still available.
/// </para>
/// </remarks>
internal static class ListElementFilterNaming
{
    /// <summary>
    /// A stable, unique name for an element type, used to build the filter input names.
    /// </summary>
    /// <remarks>
    /// It has to be injective over the element types that get bound, because each one
    /// produces its own closed generic filter type and two types cannot share a GraphQL
    /// name. That is why <c>double</c> and <c>DateTimeOffset</c> do not reuse the
    /// <c>Float</c> / <c>DateTime</c> names HotChocolate gives them by default: stock
    /// HotChocolate can share one input type between <c>float[]</c> and <c>double[]</c>,
    /// but the restricted element types are distinct CLR types and would collide.
    /// </remarks>
    public static string ScalarName(Type type)
    {
        return type switch
        {
            _ when type == typeof(int) => "Int",
            _ when type == typeof(short) => "Short",
            _ when type == typeof(long) => "Long",
            _ when type == typeof(byte) => "Byte",
            _ when type == typeof(float) => "Float",
            _ when type == typeof(double) => "Double",
            _ when type == typeof(decimal) => "Decimal",
            _ when type == typeof(bool) => "Boolean",
            _ when type == typeof(string) => "String",
            _ when type == typeof(Guid) => "UUID",
            _ when type == typeof(DateTime) => "DateTime",
            _ => type.Name,
        };
    }

    /// <summary>
    /// The element input's name. Deliberately outside HotChocolate's
    /// <c>{Scalar}OperationFilterInput</c> / <c>List{Scalar}OperationFilterInput</c>
    /// namespace: a collection whose element cannot be restricted (a nullable one) keeps
    /// the stock types, and reusing a stock name for a restricted type would collide with
    /// it at schema build.
    /// </summary>
    public static string ElementTypeName(Type elementType) =>
        ScalarName(elementType) + "ElementFilterInput";

    /// <summary>The list input's name, wrapping <see cref="ElementTypeName"/>.</summary>
    public static string ListTypeName(Type elementType) =>
        "List" + ScalarName(elementType) + "ElementFilterInput";
}

/// <summary>
/// The list filter for a scalar collection. Identical to
/// <see cref="ListFilterInputType{T}"/> apart from keeping HotChocolate's stock name
/// while pointing <c>some</c>/<c>all</c>/<c>none</c> at the restricted element type.
/// </summary>
/// <typeparam name="TElement">The collection's element runtime type.</typeparam>
/// <typeparam name="TElementFilter">The restricted element filter input.</typeparam>
internal sealed class ListElementFilterInputType<TElement, TElementFilter>
    : ListFilterInputType<TElementFilter>
    where TElementFilter : FilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ListTypeName(typeof(TElement)));
    }
}

/// <summary>Enum elements, without <c>neq</c>.</summary>
internal sealed class EnumListElementFilterInputType<TEnum> : EnumOperationFilterInputType<TEnum>
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(TEnum)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}

/// <summary>Comparable scalar elements (numeric, date, Guid), without <c>neq</c>.</summary>
internal sealed class ComparableListElementFilterInputType<T>
    : ComparableOperationFilterInputType<T>
    where T : struct
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(T)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}

/// <summary>String elements, without <c>neq</c>.</summary>
internal sealed class StringListElementFilterInputType : StringOperationFilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(string)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}

/// <summary>Boolean elements, without <c>neq</c>.</summary>
internal sealed class BooleanListElementFilterInputType : BooleanOperationFilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(bool)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}
