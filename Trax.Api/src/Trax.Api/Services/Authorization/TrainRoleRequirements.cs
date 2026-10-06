using System.Reflection;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Services.Authorization;

/// <summary>
/// The role requirements a train's <see cref="TraxAuthorizeAttribute"/>s impose: one role list
/// per attribute that names roles, every one of which the caller must satisfy by holding at least
/// one of its roles. Separate attributes combine with AND, as separate <c>[Authorize]</c>
/// attributes do in ASP.NET Core and as <c>@authorize</c> directives do on a query model.
/// </summary>
/// <remarks>
/// <see cref="TrainRegistration.RequiredRoles"/> is the union of every attribute's roles, which
/// cannot tell <c>[TraxAuthorize(Roles = "A,B")]</c> from two attributes naming <c>A</c> and
/// <c>B</c>. The lists are read again from the attributes, over the same carriers discovery reads
/// (the implementation, its base chain and every interface it implements). A registration whose
/// attributes do not account for its <see cref="TrainRegistration.RequiredRoles"/>, one built by
/// hand rather than by discovery, is taken at its word as a single list.
/// </remarks>
internal static class TrainRoleRequirements
{
    public static IReadOnlyList<IReadOnlyList<string>> For(TrainRegistration registration)
    {
        if (registration.RequiredRoles.Count == 0)
            return [];

        var sets = new List<string[]>();
        foreach (var attribute in Attributes(registration.ImplementationType))
        {
            var roles = attribute
                .Roles?.Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                )
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (
                roles is not { Length: > 0 }
                || sets.Any(s => s.SequenceEqual(roles, StringComparer.Ordinal))
            )
                continue;
            sets.Add(roles);
        }

        var declared = sets.SelectMany(s => s).ToHashSet(StringComparer.Ordinal);
        return declared.SetEquals(registration.RequiredRoles) ? sets : [registration.RequiredRoles];
    }

    private static IEnumerable<TraxAuthorizeAttribute> Attributes(Type implementationType) =>
        new[] { implementationType }
            .Concat(implementationType.GetInterfaces())
            .SelectMany(t => t.GetCustomAttributes<TraxAuthorizeAttribute>(inherit: true))
            .Distinct();
}
