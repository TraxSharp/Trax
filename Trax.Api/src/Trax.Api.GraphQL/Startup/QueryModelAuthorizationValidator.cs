using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fail-loud startup check that every <see cref="HotChocolate.Authorization"/>
/// directive emitted for a <c>[TraxQueryModel]</c> entity points at a policy
/// the host has actually registered. Without this, a typoed
/// <c>[TraxAuthorize(Policy = "AdmnPolicy")]</c> would compile, ship, and
/// silently deny every caller at runtime — the worst-of-both: insecure to
/// reason about (looks gated, isn't), and broken in production.
/// </summary>
/// <remarks>
/// Mirrors <c>TraxGraphQLAuthPolicyValidator</c>, which performs the same
/// check for the endpoint-level <c>RequireAuthorization()</c> opt-in.
/// </remarks>
internal sealed class QueryModelAuthorizationValidator(
    GraphQLConfiguration configuration,
    IAuthorizationPolicyProvider policyProvider
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reg in configuration.ModelRegistrations)
        {
            foreach (var attr in reg.AuthorizeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attr.Policy))
                    continue;
                if (!seen.Add(attr.Policy))
                    continue;

                var policy = await policyProvider.GetPolicyAsync(attr.Policy);
                if (policy is null)
                    throw new InvalidOperationException(
                        $"[TraxAuthorize(Policy = \"{attr.Policy}\")] on '{reg.EntityType.FullName}' "
                            + "references an authorization policy that is not registered. Call "
                            + $"`services.AddAuthorization(opts => opts.AddPolicy(\"{attr.Policy}\", ...))` "
                            + "during host setup, or pass an existing policy name to [TraxAuthorize]."
                    );
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
