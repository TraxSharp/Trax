using HotChocolate.Authorization;
using HotChocolate.Internal;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Turns a set of <see cref="TraxAuthorizeAttribute"/> into HotChocolate <c>@authorize</c>
/// directives. Shared so a query-model entity, a resolver and a namespace field carrying the same
/// attributes get identical rules: the combinator semantics live here once rather than at each
/// call site.
/// </summary>
/// <remarks>
/// The semantics are ASP.NET Core's for multiple <c>[Authorize]</c> attributes, where each
/// attribute is a requirement of its own and every requirement must pass:
/// <list type="bullet">
/// <item>Bare <c>[TraxAuthorize]</c> with no policy or roles emits an empty <c>@authorize</c>,
/// which the HotChocolate authorization middleware treats as "require authenticated user."</item>
/// <item>Every <see cref="TraxAuthorizeAttribute.Policy"/> becomes its own directive.</item>
/// <item>Every attribute's <see cref="TraxAuthorizeAttribute.Roles"/> list becomes its own
/// directive, so the principal must hold at least one role from each attribute's list. A
/// comma-separated list within one attribute is any of; separate attributes are all of.</item>
/// </list>
/// HotChocolate evaluates every <c>@authorize</c> on a field or type, and all must allow, so one
/// directive per requirement is exactly that combination. An attribute added at a narrower scope,
/// a method under a gated extension class or a second <c>GateOperations(roles:)</c> call, can
/// therefore only narrow who gets in.
/// </remarks>
internal static class AuthorizeDirectives
{
    public static void Apply<TEntity>(
        IObjectTypeDescriptor<TEntity> descriptor,
        IReadOnlyList<TraxAuthorizeAttribute> attributes
    )
        where TEntity : class
    {
        ExtractRules(attributes, out var policies, out var roleSets);

        foreach (var policy in policies)
            descriptor.Authorize(policy, ApplyPolicy.BeforeResolver);

        foreach (var roles in roleSets)
            descriptor.Authorize(roles);

        if (policies.Length == 0 && roleSets.Length == 0 && attributes.Count > 0)
            descriptor.Authorize(ApplyPolicy.BeforeResolver);
    }

    public static void Apply(
        IObjectFieldDescriptor descriptor,
        IReadOnlyList<TraxAuthorizeAttribute> attributes
    )
    {
        ExtractRules(attributes, out var policies, out var roleSets);

        foreach (var policy in policies)
            descriptor.Authorize(policy, ApplyPolicy.BeforeResolver);

        foreach (var roles in roleSets)
            descriptor.Authorize(roles);

        if (policies.Length == 0 && roleSets.Length == 0 && attributes.Count > 0)
            descriptor.Authorize(ApplyPolicy.BeforeResolver);
    }

    /// <summary>
    /// Emits the same directives onto a type-system configuration, for the places that run inside
    /// a type interceptor and have no descriptor to call. One <c>@authorize</c> per policy and one
    /// per attribute's role list, so an entity and a resolver carrying the same attributes get
    /// the same rules.
    /// </summary>
    public static void Emit(
        IDirectiveConfigurationProvider target,
        IReadOnlyList<TraxAuthorizeAttribute> attributes,
        ITypeInspector inspector
    )
    {
        ExtractRules(attributes, out var policies, out var roleSets);

        // ConfigurationHelper is how HotChocolate itself turns a directive instance into a
        // configuration: it builds the type reference from the inspector, which is not something
        // a caller can construct.
        foreach (var policy in policies)
            target.AddDirective(
                new AuthorizeDirective(policy, apply: ApplyPolicy.BeforeResolver),
                inspector
            );

        foreach (var roles in roleSets)
            target.AddDirective(
                new AuthorizeDirective(roles, apply: ApplyPolicy.BeforeResolver),
                inspector
            );

        if (policies.Length == 0 && roleSets.Length == 0)
            target.AddDirective(new AuthorizeDirective(ApplyPolicy.BeforeResolver), inspector);
    }

    /// <summary>
    /// Reduces a set of <see cref="TraxAuthorizeAttribute"/> instances to the requirements they
    /// impose: the distinct policies, every one of which must pass, and one role list per
    /// attribute that names roles, every one of which must be satisfied by holding at least one
    /// of its roles. Identical requirements are reduced to one.
    /// </summary>
    public static void ExtractRules(
        IReadOnlyList<TraxAuthorizeAttribute> attributes,
        out string[] policies,
        out string[][] roleSets
    )
    {
        var sets = new List<string[]>();
        foreach (var attribute in attributes)
        {
            var roles = ParseRoles(attribute.Roles);
            if (roles.Length == 0 || sets.Any(s => s.SequenceEqual(roles, StringComparer.Ordinal)))
                continue;
            sets.Add(roles);
        }
        roleSets = [.. sets];

        policies = attributes
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The roles a comma-separated list names, trimmed, distinct and in a stable order, or none
    /// when the list is <c>null</c> or holds only separators and whitespace.
    /// </summary>
    public static string[] ParseRoles(string? roles) =>
        roles is null
            ? []
            :
            [
                .. roles
                    .Split(
                        ',',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                    )
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
}
