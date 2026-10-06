using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace Trax.Api.GraphQL.Projection;

/// <summary>
/// Finds the properties that make up an entity's primary key, following EF Core's
/// data-annotation and naming conventions.
/// </summary>
/// <remarks>
/// The EF model itself is not reachable here: requirements are declared while the schema
/// is being built, and HotChocolate builds it from a container that does not hold the
/// host's <c>DbContext</c>. The conventions below cover every key EF can infer from the
/// entity class, which is how Trax query models are written.
/// <para>
/// A key configured only through the fluent API (<c>HasKey</c> in <c>OnModelCreating</c>)
/// with no matching annotation or conventional name is not visible here, and the resolver
/// returns nothing for it. Such a resolver declares what it reads with
/// <c>[Parent(requires:)]</c> instead.
/// </para>
/// </remarks>
internal static class EntityKeyResolver
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// The key property names for <paramref name="entityType"/>, in key order, or an empty
    /// array when no key can be inferred.
    /// </summary>
    public static string[] GetKeyPropertyNames(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var properties = entityType.GetProperties(PublicInstance);

        // 1. Explicit [Key]. A composite key orders its parts by [Column(Order = n)],
        //    matching how EF Core reads the same annotations.
        var annotated = properties
            .Where(p => p.IsDefined(typeof(KeyAttribute), inherit: true))
            .OrderBy(p => p.GetCustomAttribute<ColumnAttribute>(inherit: true)?.Order ?? 0)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name)
            .ToArray();

        if (annotated.Length > 0)
            return annotated;

        // 2. EF's conventional names: `Id`, then `<TypeName>Id`.
        var byConvention =
            FindProperty(properties, "Id") ?? FindProperty(properties, entityType.Name + "Id");

        return byConvention is null ? [] : [byConvention.Name];
    }

    private static PropertyInfo? FindProperty(PropertyInfo[] properties, string name) =>
        properties.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
        );
}
