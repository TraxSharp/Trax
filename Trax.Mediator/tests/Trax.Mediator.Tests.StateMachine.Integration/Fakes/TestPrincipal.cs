using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>The user behind the current request, set by each test.</summary>
public sealed class TestPrincipal : ISnapshotPrincipal
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CurrentUserKey => Current.Value;

    public static void Become(string? userKey) => Current.Value = userKey;
}
