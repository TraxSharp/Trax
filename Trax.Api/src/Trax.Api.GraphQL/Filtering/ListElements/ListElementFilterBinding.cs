using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using HotChocolate.Data.Filters;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>
/// Points scalar collection properties on query models at the restricted element filter
/// inputs in <see cref="ListElementFilterInputTypes"/>, so the untranslatable
/// <c>some/all/none: { neq: ... }</c> never reaches the schema.
/// </summary>
/// <remarks>
/// Binding is by property <i>runtime type</i> (<c>Badge[]</c>, <c>List&lt;string&gt;</c>),
/// which is the level HotChocolate resolves filter types at. That covers every route to
/// a filter input at once: the auto-generated one, an <c>ExposeAs</c>-restricted one, and
/// a custom <c>AddFilterType</c> override. Scalar properties of the same element type
/// keep the stock input, <c>neq</c> included.
/// </remarks>
internal static class ListElementFilterBinding
{
    private static readonly HashSet<Type> ComparableScalars =
    [
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(char),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
    ];

    private static readonly HashSet<Type> CollectionDefinitions =
    [
        typeof(List<>),
        typeof(HashSet<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(IEnumerable<>),
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
    ];

    private static readonly MethodInfo BindRuntimeTypeMethod = typeof(IFilterConventionDescriptor)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(m => m.Name == "BindRuntimeType" && m.GetGenericArguments().Length == 2);

    /// <summary>
    /// Collects one binding per distinct scalar collection runtime type found across the
    /// supplied entity types.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<Type, Type>> Discover(IEnumerable<Type> entityTypes)
    {
        var bindings = new Dictionary<Type, Type>();

        foreach (var entityType in entityTypes.Distinct())
        {
            foreach (
                var property in entityType.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                )
            )
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                if (property.GetCustomAttribute<NotMappedAttribute>() is not null)
                    continue;

                if (bindings.ContainsKey(property.PropertyType))
                    continue;

                if (GetScalarElementType(property.PropertyType) is not { } element)
                    continue;

                if (BuildListFilterType(element) is not { } filterType)
                    continue;

                bindings[property.PropertyType] = filterType;
            }
        }

        return [.. bindings];
    }

    /// <summary>Registers the discovered bindings on the filter convention.</summary>
    public static void Apply(
        IFilterConventionDescriptor descriptor,
        IReadOnlyList<KeyValuePair<Type, Type>> bindings
    )
    {
        foreach (var (runtimeType, filterType) in bindings)
            BindRuntimeTypeMethod.MakeGenericMethod(runtimeType, filterType).Invoke(descriptor, []);
    }

    /// <summary>
    /// True when the property is a collection of scalars, and therefore maps to an array
    /// column rather than to a join.
    /// </summary>
    public static bool IsScalarCollection(Type propertyType) =>
        GetScalarElementType(propertyType) is not null;

    /// <summary>
    /// The element type when the property is a collection of scalars, otherwise null.
    /// Navigation collections return null: their element is an entity, and they are joins
    /// rather than array columns.
    /// </summary>
    private static Type? GetScalarElementType(Type propertyType)
    {
        // string is IEnumerable<char> but maps to a single column.
        if (propertyType == typeof(string))
            return null;

        // byte[] maps to a binary column (bytea), not to an array of scalars.
        if (propertyType == typeof(byte[]))
            return null;

        Type? element = null;

        if (propertyType.IsArray && propertyType.GetArrayRank() == 1)
            element = propertyType.GetElementType();
        else if (
            propertyType.IsGenericType
            && CollectionDefinitions.Contains(propertyType.GetGenericTypeDefinition())
        )
            element = propertyType.GetGenericArguments()[0];

        return element is not null && IsScalar(element) ? element : null;
    }

    private static bool IsScalar(Type element)
    {
        var underlying = Nullable.GetUnderlyingType(element) ?? element;

        return underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(bool)
            || ComparableScalars.Contains(underlying);
    }

    /// <summary>
    /// Closes <see cref="ListElementFilterInputType{TElement,TElementFilter}"/> over the
    /// element filter that matches the element's kind, or returns null when the element
    /// has no HotChocolate operation filter input to restrict.
    /// </summary>
    private static Type? BuildListFilterType(Type element)
    {
        var elementFilter = BuildElementFilterType(element);
        if (elementFilter is null)
            return null;

        return typeof(ListElementFilterInputType<,>).MakeGenericType(element, elementFilter);
    }

    private static Type? BuildElementFilterType(Type element)
    {
        // Nullable elements are left on HotChocolate's stock input. The C# `struct`
        // constraint on ComparableOperationFilterInputType<T> means "non-nullable value
        // type", so Nullable<T> cannot close it, and a nullable enum has no non-nullable
        // enum filter to derive from either. Binding one throws at startup rather than
        // producing a restricted type, so these keep the stock behaviour instead.
        if (Nullable.GetUnderlyingType(element) is not null)
            return null;

        if (element.IsEnum)
            return typeof(EnumListElementFilterInputType<>).MakeGenericType(element);

        if (element == typeof(string))
            return typeof(StringListElementFilterInputType);

        if (element == typeof(bool))
            return typeof(BooleanListElementFilterInputType);

        if (element.IsValueType && ComparableScalars.Contains(element))
            return typeof(ComparableListElementFilterInputType<>).MakeGenericType(element);

        return null;
    }
}
