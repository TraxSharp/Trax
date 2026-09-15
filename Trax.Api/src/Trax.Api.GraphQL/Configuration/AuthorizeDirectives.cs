using HotChocolate.Authorization;
using HotChocolate.Types;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Turns a set of <see cref="TraxAuthorizeAttribute"/> into HotChocolate <c>@authorize</c>
/// directives. Shared so a train, a query-model entity and a namespace field carrying the same
/// attribute get identical rules: the combinator semantics live here once rather than at each
/// call site.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Bare <c>[TraxAuthorize]</c> with no policy or roles emits an empty <c>@authorize</c>,
/// which the HotChocolate authorization middleware treats as "require authenticated user."</item>
/// <item>Every <see cref="TraxAuthorizeAttribute.Policy"/> becomes its own directive;
/// HotChocolate evaluates them with AND semantics.</item>
/// <item>All <see cref="TraxAuthorizeAttribute.Roles"/> values across every attached attribute
/// are unioned (CSV split, trimmed, distinct) and emitted as a single directive, so the
/// principal must hold at least one. Multiple role directives would AND the OR-sets together,
/// which is not the documented contract.</item>
/// </list>
/// </remarks>
internal static class AuthorizeDirectives
{
    public static void Apply<TEntity>(
        IObjectTypeDescriptor<TEntity> descriptor,
        IReadOnlyList<TraxAuthorizeAttribute> attributes
    )
        where TEntity : class
    {
        ExtractRules(attributes, out var policies, out var roles);

        foreach (var policy in policies)
            descriptor.Authorize(policy, ApplyPolicy.BeforeResolver);

        if (roles.Length > 0)
            descriptor.Authorize(roles);
        else if (policies.Length == 0 && attributes.Count > 0)
            descriptor.Authorize(ApplyPolicy.BeforeResolver);
    }

    public static void Apply(
        IObjectFieldDescriptor descriptor,
        IReadOnlyList<TraxAuthorizeAttribute> attributes
    )
    {
        ExtractRules(attributes, out var policies, out var roles);

        foreach (var policy in policies)
            descriptor.Authorize(policy, ApplyPolicy.BeforeResolver);

        if (roles.Length > 0)
            descriptor.Authorize(roles);
        else if (policies.Length == 0 && attributes.Count > 0)
            descriptor.Authorize(ApplyPolicy.BeforeResolver);
    }

    /// <summary>
    /// Reduces a set of <see cref="TraxAuthorizeAttribute"/> instances into the distinct policy
    /// and role lists used to emit <c>@authorize</c> directives. Policies AND across attributes;
    /// roles OR within an attribute (CSV split) and OR across attributes (unioned). The semantics
    /// mirror <see cref="Trax.Api.Services.Authorization.TrainAuthorizationService"/>'s train-side
    /// enforcement so a model and a train that declare the same <c>[TraxAuthorize]</c> shape have
    /// identical access rules.
    /// </summary>
    public static void ExtractRules(
        IReadOnlyList<TraxAuthorizeAttribute> attributes,
        out string[] policies,
        out string[] roles
    )
    {
        roles = attributes
            .Where(a => a.Roles is not null)
            .SelectMany(a => a.Roles!.Split(',', StringSplitOptions.TrimEntries))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        policies = attributes
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
