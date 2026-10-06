using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Broadcaster.SignalR.UnitTests;

/// <summary>
/// Each run gets a hook that forwards to the one shared dispatcher instead of the dispatcher
/// itself. The forwarder must pass on every lifecycle event, including one added to
/// <see cref="ITrainLifecycleHook"/> later as a default interface method, which it would otherwise
/// silently inherit as a no-op.
/// </summary>
[TestFixture]
public class SignalRLifecycleHookForwarderTests
{
    [Test]
    public async Task TheForwarderImplementsEveryLifecycleHookMember()
    {
        await using var dispatcher = NewDispatcher(out _);
        var forwarderType = ForwarderFrom(dispatcher).GetType();
        var map = forwarderType.GetInterfaceMap(typeof(ITrainLifecycleHook));

        var inherited = map
            .TargetMethods.Where(target => target.DeclaringType != forwarderType)
            .Select(target => target.Name)
            .ToList();

        inherited
            .Should()
            .BeEmpty(
                "every ITrainLifecycleHook member must be forwarded to the shared dispatcher; "
                    + "a member left to its default implementation drops that event"
            );
        map.InterfaceMethods.Should().HaveCount(typeof(ITrainLifecycleHook).GetMethods().Length);
    }

    [Test]
    public async Task EveryLifecycleEventReachesTheClientsThroughTheForwarder()
    {
        await using var dispatcher = NewDispatcher(out var client);
        var hook = ForwarderFrom(dispatcher);
        var metadata = NewRun();

        await hook.OnStarted(metadata, CancellationToken.None);
        await hook.OnCompleted(metadata, CancellationToken.None);
        await hook.OnFailed(metadata, new InvalidOperationException("x"), CancellationToken.None);
        await hook.OnCancelled(metadata, CancellationToken.None);
        await hook.OnStateChanged(metadata, CancellationToken.None);
        await dispatcher.StopAsync(CancellationToken.None);

        var sent = client
            .ReceivedCalls()
            .Select(call => ((TraxClientEvent)call.GetArguments()[0]!).EventType)
            .ToList();
        sent.Should()
            .BeEquivalentTo(["Started", "Completed", "Failed", "Cancelled", "StateChanged"]);
    }

    [Test]
    public void TheForwarderTargetsTheSingletonTheEventHandlerPathUses()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        new TraxBuilder(services, new EffectRegistry()).AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseSignalRHub())
        );
        using var sp = services.BuildServiceProvider();

        var singleton = sp.GetRequiredService<SignalRTrainEventDispatcher>();
        var handler = sp.GetServices<ITrainEventHandler>()
            .OfType<SignalRTrainEventDispatcher>()
            .Single();
        var forwarder = sp.GetServices<ITrainLifecycleHookFactory>()
            .OfType<SignalRTrainEventDispatcherFactory>()
            .Single()
            .Create();

        var target = forwarder
            .GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(field => field.FieldType == typeof(SignalRTrainEventDispatcher))
            .GetValue(forwarder);

        handler.Should().BeSameAs(singleton);
        target.Should().BeSameAs(singleton);
    }

    private static readonly AsyncLocal<string?> Ambient = new();

    [Test]
    public async Task The_send_loop_does_not_carry_the_flow_that_created_the_dispatcher()
    {
        var seen = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        // The dispatcher is often first resolved on a train run's flow, and its loop outlives it.
        Ambient.Value = "the creating run";
        await using var dispatcher = NewDispatcher(out var client);
        Ambient.Value = null;
        client
            .TrainEvent(Arg.Any<object>())
            .Returns(_ =>
            {
                seen.TrySetResult(Ambient.Value);
                return Task.CompletedTask;
            });

        await ForwarderFrom(dispatcher).OnStarted(NewRun(), CancellationToken.None);

        (await seen.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeNull();
    }

    private static ITrainLifecycleHook ForwarderFrom(SignalRTrainEventDispatcher dispatcher) =>
        new SignalRTrainEventDispatcherFactory(dispatcher).Create();

    private static SignalRTrainEventDispatcher NewDispatcher(out ITraxTrainEventClient client)
    {
        var hub = Substitute.For<IHubContext<TraxTrainEventHub, ITraxTrainEventClient>>();
        var clients = Substitute.For<IHubClients<ITraxTrainEventClient>>();
        client = Substitute.For<ITraxTrainEventClient>();
        clients.All.Returns(client);
        hub.Clients.Returns(clients);

        var options = (SignalRSinkOptions)
            Activator.CreateInstance(typeof(SignalRSinkOptions), nonPublic: true)!;
        return new SignalRTrainEventDispatcher(hub, options.Build());
    }

    private static Metadata NewRun()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "T.IDoThing",
                ExternalId = "run-1",
                Input = null,
            }
        );
        metadata.TrainState = TrainState.InProgress;
        return metadata;
    }
}
