using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// The properties a <c>[TraxQueryModel]</c> entity exposes, which is the one set its object type,
/// its filter input and its sort input are all built from.
/// </summary>
/// <remarks>
/// <c>ExposeAs</c> narrows the set to the interface's properties and <c>BindFields = Explicit</c>
/// to the <c>[Column]</c> properties. A model with neither exposes every public property, and
/// HotChocolate's own inference already builds the inputs from that set.
/// </remarks>
internal static class QueryModelFieldSet
{
    /// <summary>
    /// The exposed properties of <paramref name="entityType"/>, or <c>null</c> when the model
    /// does not narrow its field set.
    /// </summary>
    public static IReadOnlyList<PropertyInfo>? Restricted(Type entityType)
    {
        var attr = entityType.GetCustomAttribute<TraxQueryModelAttribute>();
        if (attr is null)
            return null;

        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        if (attr.ExposeAs is { } exposeAs)
        {
            var allowed = QueryModelTypeModule.GetExposedPropertyNames(exposeAs);
            return properties.Where(p => allowed.Contains(p.Name)).ToList();
        }

        if (attr.BindFields == FieldBindingBehavior.Explicit)
            return properties
                .Where(p => p.GetCustomAttribute<ColumnAttribute>() is not null)
                .ToList();

        return null;
    }

    internal static void BindFilterFields<TEntity>(IFilterInputTypeDescriptor<TEntity> descriptor)
    {
        if (Restricted(typeof(TEntity)) is not { } properties)
            return;

        descriptor.BindFieldsExplicitly();
        foreach (var prop in properties)
            FilterFieldMethod
                .MakeGenericMethod(typeof(TEntity), prop.PropertyType)
                .Invoke(null, [descriptor, Selector<TEntity>(prop)]);
    }

    internal static void BindSortFields<TEntity>(ISortInputTypeDescriptor<TEntity> descriptor)
    {
        if (Restricted(typeof(TEntity)) is not { } properties)
            return;

        descriptor.BindFieldsExplicitly();
        foreach (var prop in properties)
            SortFieldMethod
                .MakeGenericMethod(typeof(TEntity), prop.PropertyType)
                .Invoke(null, [descriptor, Selector<TEntity>(prop)]);
    }

    private static readonly MethodInfo FilterFieldMethod = typeof(QueryModelFieldSet).GetMethod(
        nameof(AddFilterField),
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static readonly MethodInfo SortFieldMethod = typeof(QueryModelFieldSet).GetMethod(
        nameof(AddSortField),
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static void AddFilterField<TEntity, TField>(
        IFilterInputTypeDescriptor<TEntity> descriptor,
        Expression<Func<TEntity, TField>> selector
    ) => descriptor.Field(selector);

    private static void AddSortField<TEntity, TField>(
        ISortInputTypeDescriptor<TEntity> descriptor,
        Expression<Func<TEntity, TField>> selector
    ) => descriptor.Field(selector);

    private static object Selector<TEntity>(PropertyInfo prop)
    {
        var param = Expression.Parameter(typeof(TEntity), "x");
        var body = Expression.Property(param, prop);
        var funcType = typeof(Func<,>).MakeGenericType(typeof(TEntity), prop.PropertyType);
        return Expression.Lambda(funcType, body, param);
    }
}

/// <summary>
/// The filter input for a <c>[TraxQueryModel]</c> entity, built from its exposed field set.
/// Bound to the entity in the filter convention, so the same input is used on the model's own
/// entry field and wherever another model's filter reaches it through a navigation.
/// </summary>
internal sealed class QueryModelFilterInputType<TEntity> : FilterInputType<TEntity>
{
    protected override void Configure(IFilterInputTypeDescriptor<TEntity> descriptor) =>
        QueryModelFieldSet.BindFilterFields(descriptor);
}

/// <summary>
/// The sort input for a <c>[TraxQueryModel]</c> entity, built from its exposed field set. See
/// <see cref="QueryModelFilterInputType{TEntity}"/>.
/// </summary>
internal sealed class QueryModelSortInputType<TEntity> : SortInputType<TEntity>
{
    protected override void Configure(ISortInputTypeDescriptor<TEntity> descriptor) =>
        QueryModelFieldSet.BindSortFields(descriptor);
}
