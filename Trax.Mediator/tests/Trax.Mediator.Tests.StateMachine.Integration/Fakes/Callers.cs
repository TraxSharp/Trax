using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>The user behind the current request, set by each test.</summary>
public sealed class TestPrincipal : ISnapshotPrincipal
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CurrentUserKey => Current.Value;

    public static void Become(string? userKey) => Current.Value = userKey;
}

/// <summary>One authorization the mediator asked for: which train, for whom, and whether the scope was trusted.</summary>
public sealed record AuthorizationCall(string Train, string? User, bool Trusted);

/// <summary>
/// The host's train authorization: records every call, and refuses the users in <see cref="Refused"/> the way an
/// enforcer refuses a caller, unless the scope is trusted.
/// </summary>
public sealed class RecordingAuthorization(IServiceProvider services) : ITrainAuthorizationService
{
    public static List<AuthorizationCall> Calls { get; } = [];

    public static HashSet<string> Refused { get; } = [];

    public Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default)
    {
        var user = services.GetRequiredService<ISnapshotPrincipal>().CurrentUserKey;
        var trusted = services.GetRequiredService<ITrustedExecutionScope>().IsTrusted;
        lock (Calls)
            Calls.Add(new AuthorizationCall(registration.ServiceType.FullName!, user, trusted));

        if (!trusted && user is not null && Refused.Contains(user))
            throw new UnauthorizedAccessException(
                $"{user} may not run {registration.ServiceTypeName}."
            );
        return Task.CompletedTask;
    }
}
