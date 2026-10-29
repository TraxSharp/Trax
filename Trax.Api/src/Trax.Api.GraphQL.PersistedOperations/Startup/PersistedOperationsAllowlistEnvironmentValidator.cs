using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Startup;

/// <summary>
/// Refuses to start a host outside Development whose persisted-operation enforcement is on
/// (<c>RequirePersisted(true)</c>) and whose allowlist (<c>AllowOperations</c> or
/// <c>AllowOperationsMatching</c>) is not empty.
/// </summary>
/// <remarks>
/// The allowlist keys on the name the request carries, so under enforcement it is a carve-out for
/// developer tools and starts only in Development. Outside it, a document that must run inline is
/// persisted and sent by its id; a probe that selects only <c>__typename</c> passes as
/// introspection. With enforcement off the allowlist only quiets the shadow log, and it starts
/// anywhere. See <c>docs/adr/0005</c>.
/// </remarks>
internal sealed class PersistedOperationsAllowlistEnvironmentValidator(
    PersistedOperationsOptions options,
    IHostEnvironment? environment = null
) : StartupGate
{
    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        // A container without a host environment is treated as not Development.
        if (!options.RequirePersisted || environment?.IsDevelopment() == true)
            return Task.CompletedTask;

        if (options.AllowedOperationNames.Count == 0 && options.AllowOperationPredicates.Count == 0)
            return Task.CompletedTask;

        throw new InvalidOperationException(
            "UsePersistedOperations requires persisted operations and declares an allowlist "
                + "(AllowOperations or AllowOperationsMatching), and the environment is "
                + $"'{environment?.EnvironmentName ?? "(none)"}'. Under RequirePersisted(true) the allowlist "
                + "starts only in Development. Persist the documents that must run here and send "
                + "them by id, or declare the allowlist only when the environment is Development "
                + "(docs/adr/0005)."
        );
    }
}
