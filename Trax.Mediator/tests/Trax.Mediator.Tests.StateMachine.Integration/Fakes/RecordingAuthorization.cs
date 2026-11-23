using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>
/// The host's train authorization: records every call, and refuses the users in <see cref="Refused"/> the way an
/// enforcer refuses a caller, unless the scope is trusted.
/// </summary>
public sealed class RecordingAuthorization(IServiceProvider services) : ITrainAuthorizationService
{
    public static IList<AuthorizationCall> Calls { get; } = [];

    public static ISet<string> Refused { get; } = new HashSet<string>(StringComparer.Ordinal);

    public Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default)
    {
        var user = services.GetRequiredService<ISnapshotPrincipal>().CurrentUserKey;
        var trusted = services.GetRequiredService<ITrustedExecutionScope>().IsTrusted;
        lock (Calls)
            Calls.Add(
                new AuthorizationCall(registration.ServiceType.FullName.NotNull(), user, trusted)
            );

        if (!trusted && user is not null && Refused.Contains(user))
            throw new UnauthorizedAccessException(
                $"{user} may not run {registration.ServiceTypeName}."
            );
        return Task.CompletedTask;
    }
}
