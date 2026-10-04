using System.Reflection;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// The authorization posture an entity that is not a <c>[TraxQueryModel]</c> declares on its own
/// class, for an entity a query model reaches through a navigation.
/// </summary>
/// <remarks>
/// HotChocolate infers an object type, a filter input and a sort input for every navigation
/// target, so such an entity is as exposed as the model that reaches it. It declares its posture
/// with the same attributes a query model uses: <c>[TraxAuthorize]</c> gates its object type
/// wherever it appears, and <c>[TraxAllowAnonymous]</c> opens it.
/// </remarks>
internal sealed record NavigationTargetPosture(
    Type EntityType,
    IReadOnlyList<TraxAuthorizeAttribute> AuthorizeAttributes,
    bool AllowAnonymous
)
{
    /// <summary>The entity carries <c>[TraxAuthorize]</c>, and Trax gates its object type.</summary>
    public bool IsGated => AuthorizeAttributes.Count > 0 && !AllowAnonymous;

    /// <summary>Reads the posture declared on <paramref name="entityType"/>.</summary>
    public static NavigationTargetPosture Read(Type entityType) =>
        new(
            entityType,
            TraxGraphQLBuilder.TraxGraphQLBuilder.DiscoverAuthorizeAttributes(entityType),
            TraxGraphQLBuilder.TraxGraphQLBuilder.DiscoverAllowAnonymous(entityType)
        );

    /// <summary>
    /// Every class a query model can reach through its public properties and methods,
    /// transitively, that is not itself a query model, with the posture each declares.
    /// </summary>
    /// <remarks>
    /// This is a superset read off the CLR types, before any schema exists, and is used only to
    /// put the declared gate on the object type HotChocolate infers. Which of these the schema
    /// really reaches, and which are entities rather than owned values, is decided after the
    /// schema is built, by <see cref="Startup.QueryModelReachValidator"/>.
    /// </remarks>
    public static IReadOnlyList<NavigationTargetPosture> Discover(IEnumerable<Type> modelTypes)
    {
        var models = modelTypes.ToHashSet();
        var seen = new HashSet<Type>(models);
        var queue = new Queue<Type>(models);
        var targets = new List<NavigationTargetPosture>();

        while (queue.TryDequeue(out var type))
        {
            foreach (var returned in MemberTypes(type))
            {
                if (Candidate(returned) is not { } target || !seen.Add(target))
                    continue;

                targets.Add(Read(target));
                queue.Enqueue(target);
            }
        }

        return targets;
    }

    /// <summary>
    /// The types an entity's public members hand back, which is what HotChocolate infers fields
    /// from: every property, and every method that returns something. A method is the same reach
    /// as a property, since HotChocolate binds <c>GetOwner()</c> as a field <c>owner</c>.
    /// </summary>
    private static IEnumerable<Type> MemberTypes(Type type)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            yield return prop.PropertyType;

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (
                method.IsSpecialName
                || method.DeclaringType == typeof(object)
                || method.ReturnType == typeof(void)
            )
                continue;

            yield return Unwrap(method.ReturnType);
        }
    }

    /// <summary>A resolver's <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c> is a <c>T</c> field.</summary>
    private static Type Unwrap(Type type) =>
        type.IsGenericType
        && (
            type.GetGenericTypeDefinition() == typeof(Task<>)
            || type.GetGenericTypeDefinition() == typeof(ValueTask<>)
        )
            ? type.GetGenericArguments()[0]
            : type;

    /// <summary>
    /// The class a member navigates to, unwrapping a collection, or <c>null</c> when the member
    /// holds a scalar.
    /// </summary>
    private static Type? Candidate(Type memberType)
    {
        var type = memberType;

        if (type != typeof(string) && !type.IsArray)
        {
            var enumerable =
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    ? type
                    : type.GetInterfaces()
                        .FirstOrDefault(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                        );
            if (enumerable is not null)
                type = enumerable.GetGenericArguments()[0];
        }
        else if (type.IsArray)
        {
            type = type.GetElementType()!;
        }

        if (!type.IsClass || type == typeof(string) || type.IsArray)
            return null;

        // The framework's own reference types (Uri, byte[] handled above, JsonDocument and the
        // like) are scalars to HotChocolate, never navigations.
        if (
            type.Namespace is { } ns
            && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal))
        )
            return null;

        return type;
    }
}
