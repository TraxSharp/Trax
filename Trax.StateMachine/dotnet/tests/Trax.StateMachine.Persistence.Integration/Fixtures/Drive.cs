using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

/// <summary>Drives a draft service on an <see cref="InstanceHost"/>, each call in a scope of its own.</summary>
internal static class Drive
{
    public static async Task<Guid> Start(InstanceHost host, Actor.Client actor, string machine)
    {
        using var scope = host.Scope();
        var started = await host.Service(scope, machine)
            .Start(actor, new JsonObject { ["title"] = "t" }, $"start-{Guid.NewGuid():N}")
            .ConfigureAwait(false);
        return started.Should().BeOfType<StartOutcome.Started>().Which.Id;
    }

    public static async Task<StartOutcome> TryStart(
        InstanceHost host,
        Actor.Client actor,
        string machine
    )
    {
        using var scope = host.Scope();
        return await host.Service(scope, machine)
            .Start(actor, new JsonObject { ["title"] = "t" }, $"start-{Guid.NewGuid():N}")
            .ConfigureAwait(false);
    }

    public static Task<AdvanceOutcome> Event(
        InstanceHost host,
        Actor.Client actor,
        string machine,
        Guid id,
        string trigger,
        JsonNode? input = null
    ) =>
        Task.Run(async () =>
        {
            using var scope = host.Scope();
            return await host.Service(scope, machine)
                .Advance(actor, id, trigger, input, $"e-{Guid.NewGuid():N}")
                .ConfigureAwait(false);
        });

    public static Task<AdvanceOutcome> Send(
        InstanceHost host,
        Actor.Client actor,
        string machine,
        Guid id,
        string? requestId = null
    ) =>
        Task.Run(async () =>
        {
            using var scope = host.Scope();
            return await host.Service(scope, machine)
                .Send(actor, id, requestId ?? $"s-{Guid.NewGuid():N}")
                .ConfigureAwait(false);
        });

    public static async Task<DiscardOutcome> Discard(
        InstanceHost host,
        Actor.Client actor,
        string machine,
        Guid id
    )
    {
        using var scope = host.Scope();
        return await host.Service(scope, machine)
            .Discard(actor, id, $"d-{Guid.NewGuid():N}")
            .ConfigureAwait(false);
    }

    public static Task<IReadOnlyList<TimerFireResult>> SweepTimers(InstanceHost host) =>
        host
            .Services.GetRequiredService<InvokeOutcomeReconciler>()
            .FireDueTimersOnce(CancellationToken.None);

    public static Task<int> SweepDrafts(InstanceHost host) =>
        host.Services.GetRequiredService<DraftSweeper>().SweepOnce();
}
