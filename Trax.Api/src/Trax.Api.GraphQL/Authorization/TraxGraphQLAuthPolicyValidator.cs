using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// Fail-loud startup check: when <c>RequireAuthorization()</c> was called on
/// the GraphQL builder, verifies that the resolved policy is actually
/// registered in the container. Catches the misconfiguration where a host
/// gates GraphQL execution but forgot to call <c>AddTraxApiKeyAuth</c> (or
/// some other <c>AddTrax*Auth</c> extension that registers the policy).
/// </summary>
internal sealed class TraxGraphQLAuthPolicyValidator(
    GraphQLConfiguration configuration,
    IAuthorizationPolicyProvider policyProvider
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.AuthorizationRequired)
            return;

        var policyName = configuration.AuthorizationPolicy ?? TraxAuthClaimTypes.TraxAuthPolicy;
        var policy = await policyProvider.GetPolicyAsync(policyName);

        if (policy is null)
            throw new InvalidOperationException(
                $"AddTraxGraphQL(graphql => graphql.RequireAuthorization({FormatPolicyArg(configuration.AuthorizationPolicy)})) "
                    + $"was called, but the authorization policy '{policyName}' is not registered. "
                    + "Register an auth scheme that contributes a policy (for example "
                    + "services.AddTraxApiKeyAuth(keys => ...)) before building the host, or pass "
                    + "an explicit existing policy name to RequireAuthorization()."
            );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string FormatPolicyArg(string? policy) =>
        policy is null ? string.Empty : $"\"{policy}\"";
}
