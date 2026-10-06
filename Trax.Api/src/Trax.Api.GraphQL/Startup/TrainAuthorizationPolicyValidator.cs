using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails the host at startup when a train's <c>[TraxAuthorize(Policy = ...)]</c> names a policy
/// the host has not registered.
/// </summary>
/// <remarks>
/// A train's policies are checked by the train authorization service when the train is run,
/// queued or streamed to a subscriber, not by a schema directive, so
/// <see cref="SchemaAuthorizationPolicyValidator"/> does not see them. Without this check the host
/// starts, and every caller of the train is refused with a masked error once ASP.NET Core's
/// authorization service throws on the unknown name. The name is reported here, to whoever runs
/// the host, and never to a client.
/// </remarks>
internal sealed class TrainAuthorizationPolicyValidator(IServiceProvider serviceProvider)
    : StartupGate
{
    protected override async Task CheckAsync(CancellationToken cancellationToken)
    {
        var discovery = serviceProvider.GetService<ITrainDiscoveryService>();
        if (discovery is null)
            return;

        var named = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var train in discovery.DiscoverTrains())
        foreach (var policy in train.RequiredPolicies)
        {
            if (string.IsNullOrWhiteSpace(policy))
                continue;
            if (!named.TryGetValue(policy, out var trains))
                named[policy] = trains = new SortedSet<string>(StringComparer.Ordinal);
            trains.Add(train.ServiceType.FullName ?? train.ServiceTypeName);
        }

        if (named.Count == 0)
            return;

        var policyProvider = serviceProvider.GetService<IAuthorizationPolicyProvider>();
        var missing = new List<string>();
        foreach (var (policy, trains) in named)
            if (policyProvider is null || await policyProvider.GetPolicyAsync(policy) is null)
                missing.Add($"'{policy}', named by [TraxAuthorize] on {string.Join(", ", trains)}");

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{missing.Count} authorization polic{(missing.Count == 1 ? "y is" : "ies are")} "
                + "named by a train but not registered:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, missing.Select(m => "  " + m))
                + Environment.NewLine
                + "Register each with services.AddAuthorization(o => o.AddPolicy(\"<name>\", ...)), "
                + "or correct the name in the train's [TraxAuthorize(Policy = ...)]. A caller of "
                + "a train gated by a missing policy could never be admitted."
        );
    }
}
