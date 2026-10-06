using System.Reflection;
using HotChocolate;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Projection;

/// <summary>
/// Declares the entity key as a projection requirement on every field of a
/// <c>[TraxQueryModel]</c> type that is not backed by an entity property.
/// </summary>
/// <remarks>
/// A field that maps to a property is projected because the caller selected it. A field
/// that does not — a hand-written <c>[ExtendObjectType]</c> resolver taking
/// <c>[Parent] TEntity</c> — reads the parent in C#, where projection cannot see what it
/// touches. Overwhelmingly what it reads is the key, because that is what a DataLoader
/// batches on, so Trax requires the key for exactly those fields.
/// <para>
/// Requirements are per-field and demand-driven: the key joins the SELECT only when a
/// request actually selects one of these fields, so a query that touches none of them
/// still projects exactly the columns it named.
/// </para>
/// <para>
/// A resolver that reads something other than the key says so with
/// <c>[Parent(requires: nameof(Entity.Column))]</c>, which HotChocolate merges with the
/// requirement declared here.
/// </para>
/// </remarks>
internal sealed class QueryModelProjectionRequirementInterceptor : TypeInterceptor
{
    private readonly Dictionary<Type, string[]> _keysByEntityType;

    public QueryModelProjectionRequirementInterceptor(GraphQLConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _keysByEntityType = configuration
            .ModelRegistrations.Where(r => r.Attribute.Projection)
            .Select(r => r.EntityType)
            .Distinct()
            .Select(entityType =>
                (entityType, key: EntityKeyResolver.GetKeyPropertyNames(entityType))
            )
            .Where(x => x.key.Length > 0)
            .ToDictionary(x => x.entityType, x => x.key);
    }

    /// <summary>
    /// Runs after type extensions are merged, so consumer-supplied
    /// <c>[ExtendObjectType]</c> fields are present on the object type by this point.
    /// </summary>
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (configuration is not ObjectTypeConfiguration objectType)
            return;

        if (!_keysByEntityType.TryGetValue(objectType.RuntimeType, out var keyProperties))
            return;

        foreach (var field in objectType.Fields)
        {
            // Introspection fields resolve off the type system, not off a parent instance.
            if (field.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            // Backed by an entity property: the caller selecting the field is what puts
            // the column in the projection, so there is nothing to require.
            if (
                field.Member is PropertyInfo property
                && property.DeclaringType == objectType.RuntimeType
            )
                continue;

            // [Parent(requires:)] on the resolver has already declared what it reads.
            // Setting requirements replaces rather than merges, so fold that declaration
            // back in: a resolver can legitimately need both its column and the key.
            var declared = ReadDeclaredRequirement(field);

            field.SetFieldRequirements(Merge(declared, keyProperties), objectType.RuntimeType);
        }
    }

    /// <summary>
    /// The requirement a resolver declared with <c>[Parent(requires:)]</c>, or
    /// <c>null</c> when it declared none.
    /// </summary>
    private static string? ReadDeclaredRequirement(ObjectFieldConfiguration field)
    {
        if (field.ResolverMember is not MethodInfo resolver)
            return null;

        var declarations = resolver
            .GetParameters()
            .Select(p => p.GetCustomAttribute<ParentAttribute>(inherit: true)?.Requires)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!.Trim())
            .ToArray();

        return declarations.Length == 0 ? null : string.Join(' ', declarations);
    }

    /// <summary>
    /// Combines an already-declared requirement with the key properties, skipping any key
    /// the declaration already names.
    /// </summary>
    private static string Merge(string? declared, string[] keyProperties)
    {
        if (string.IsNullOrWhiteSpace(declared))
            return string.Join(' ', keyProperties);

        var alreadyNamed = declared.Split(
            [' ', '\t', '\n', '\r', '{', '}'],
            StringSplitOptions.RemoveEmptyEntries
        );

        var missing = keyProperties.Where(k => !alreadyNamed.Contains(k, StringComparer.Ordinal));

        return string.Join(' ', new[] { declared.Trim() }.Concat(missing));
    }
}
