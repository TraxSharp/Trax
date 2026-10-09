using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

namespace Trax.Mediator.Tests.StateMachine.Integration.IntegrationTests;

/// <summary>
/// The two ways <see cref="IMachineInstances.Start{TMachine}"/> fails rather than returning an instance: the write
/// that creates it and queues its first run is refused, or it loses a race to create the instance and then finds
/// none. Either way it throws, naming the instance, and leaves no row and no run behind.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class SystemInstanceStartFailureTests(StoreProvider provider)
{
    private InvokeHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        ObservedLauncher.Fault = LaunchFault.None;
        ObservedLauncher.Observe = null;
        HidingStore.Hide = false;
        _host = InvokeHost.Create(
            provider,
            new HostOptions
            {
                Configure = services =>
                {
                    ObservedLauncher.Install(services);
                    HidingStore.Install(services);
                },
            }
        );
    }

    [TearDown]
    public void TearDown()
    {
        ObservedLauncher.Fault = LaunchFault.None;
        HidingStore.Hide = false;
        _host.Dispose();
    }

    [Test]
    public async Task A_refused_first_run_fails_the_start_and_creates_nothing()
    {
        var key = MachineKey.Of("refused", Guid.NewGuid().ToString());
        var id = MachineInstanceId.For(PartitionMachine.MachineId, key);
        ObservedLauncher.Fault = LaunchFault.Forbidden;

        var start = () => _host.Start<PartitionMachine>(key);

        var thrown = await start.Should().ThrowAsync<InvalidOperationException>();
        thrown
            .Which.Message.Should()
            .Contain(
                $"The system instance {id} of '{PartitionMachine.MachineId}' could not be started"
            )
            .And.Contain("invoke-forbidden");
        (await _host.Row(id, PartitionMachine.MachineId))
            .Should()
            .BeNull("the row and its run are one write, and it was refused");
        (await _host.Runs(id)).Should().BeEmpty();
    }

    [Test]
    public async Task A_start_that_loses_the_create_and_then_finds_no_instance_fails()
    {
        // The row exists, so the start's insert loses, but its reads no longer see it: what a start sees when
        // the instance that won the race is deleted between the insert and the read that follows it.
        var key = MachineKey.Of("vanished", Guid.NewGuid().ToString());
        var id = (await _host.Start<PartitionMachine>(key)).Id;
        HidingStore.Hide = true;

        var start = () => _host.Start<PartitionMachine>(key);

        await start
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"The system instance {id} of '{PartitionMachine.MachineId}' could not be created, and none exists."
            );
        (await _host.Runs(id))
            .Should()
            .ContainSingle("the losing start queues nothing; the one run is the winner's");
    }

    /// <summary>
    /// Wraps the registered <see cref="IMachineInstanceStore"/>, and while <see cref="Hide"/> is set answers every
    /// read of a system instance with nothing, as though the row had just been deleted.
    /// </summary>
    internal class HidingStore : DispatchProxy
    {
        public static bool Hide { get; set; }

        private IMachineInstanceStore _inner = null!;

        public static void Install(IServiceCollection services)
        {
            var registered = services.Last(d => d.ServiceType == typeof(IMachineInstanceStore));
            services.Remove(registered);
            services.AddScoped(sp =>
            {
                var inner = (IMachineInstanceStore)registered.ImplementationFactory!(sp);
                var proxy = Create<IMachineInstanceStore, HidingStore>();
                ((HidingStore)(object)proxy)._inner = inner;
                return proxy;
            });
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (
                Hide
                && method!.Name == nameof(IMachineInstanceStore.Get)
                && args is [DraftOwner { Kind: Trax.Effect.Enums.SnapshotOwnerKind.System }, ..]
            )
                return Task.FromResult<StoredSnapshot?>(null);

            try
            {
                return method!.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }
    }
}
