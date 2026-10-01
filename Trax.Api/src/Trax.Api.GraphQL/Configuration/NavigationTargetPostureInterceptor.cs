using HotChocolate.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Puts a navigation target's declared <c>[TraxAuthorize]</c> on the object type HotChocolate
/// infers for it, so the entity is gated wherever it appears, as a <c>[TraxQueryModel]</c>
/// entity's type is.
/// </summary>
/// <remarks>
/// The type is inferred by HotChocolate from the navigation, so there is no descriptor of Trax's
/// to configure. The interceptor runs at <c>OnBeforeRegisterDependencies</c>, the same point
/// where Trax emits a resolver's directive, which is early enough for HotChocolate to turn the
/// directive into middleware on every field of the type.
/// </remarks>
internal sealed class NavigationTargetPostureInterceptor(GraphQLConfiguration configuration)
    : TypeInterceptor
{
    private readonly Dictionary<Type, NavigationTargetPosture> _gated = configuration
        .NavigationTargets.Where(t => t.IsGated)
        .ToDictionary(t => t.EntityType);

    public override void OnBeforeRegisterDependencies(
        ITypeDiscoveryContext discoveryContext,
        TypeSystemConfiguration configuration
    )
    {
        if (discoveryContext.Type is not ObjectType)
            return;

        if (configuration is not ObjectTypeConfiguration objectType)
            return;

        if (!_gated.TryGetValue(objectType.RuntimeType, out var target))
            return;

        AuthorizeDirectives.Emit(
            objectType,
            target.AuthorizeAttributes,
            discoveryContext.TypeInspector
        );
    }
}
