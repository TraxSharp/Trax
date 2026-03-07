using System.Globalization;
using System.Reflection;
using FluentAssertions;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;
using Trax.Effect.Enums;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

[TestFixture]
public class GraphQLTrainEventHandlerTests
{
    #region Attribute Gating

    [Test]
    public async Task HandleAsync_EnabledTrain_ForwardsEvent()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "Namespace.MyTrain");
        var message = CreateMessage("Completed", "Completed", "Namespace.MyTrain");

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCompleted));
    }

    [Test]
    public async Task HandleAsync_DisabledTrain_SkipsEvent()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "Namespace.MyTrain");
        var message = CreateMessage("Completed", "Completed", "Namespace.OtherTrain");

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Event Type Routing

    [Test]
    public async Task HandleAsync_Started_SendsToCorrectTopic()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage("Started", "InProgress", "My.Train");

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainStarted));
    }

    [Test]
    public async Task HandleAsync_Completed_SendsToCorrectTopic()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage("Completed", "Completed", "My.Train");

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCompleted));
    }

    [Test]
    public async Task HandleAsync_Failed_SendsToCorrectTopic()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage(
            "Failed",
            "Failed",
            "My.Train",
            failureStep: "StepA",
            failureReason: "boom"
        );

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainFailed));
    }

    [Test]
    public async Task HandleAsync_Cancelled_SendsToCorrectTopic()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage("Cancelled", "Cancelled", "My.Train");

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCancelled));
    }

    [Test]
    public async Task HandleAsync_StateChanged_SendsToCorrectTopic()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage("StateChanged", "InProgress", "My.Train");

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainStateChanged));
    }

    [Test]
    public async Task HandleAsync_UnknownEventType_DoesNotSend()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage("Unknown", "Completed", "My.Train");

        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Service vs Implementation Type Name Matching

    [Test]
    public async Task HandleAsync_TrainNameMatchesServiceType_ForwardsEvent()
    {
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            broadcastEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var handler = new GraphQLTrainEventHandler(sender, discovery);

        var message = CreateMessage("Completed", "Completed", "Namespace.IMyTrain");
        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().ContainSingle();
    }

    [Test]
    public async Task HandleAsync_TrainNameMatchesImplementationType_SkipsEvent()
    {
        // metadata.Name should always be the canonical (interface) name.
        // Implementation name should NOT match — this enforces the standardization.
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            broadcastEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var handler = new GraphQLTrainEventHandler(sender, discovery);

        var message = CreateMessage("Completed", "Completed", "Namespace.MyTrain");
        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    [Test]
    public async Task HandleAsync_TrainNameMatchesNeitherType_SkipsEvent()
    {
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            broadcastEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var handler = new GraphQLTrainEventHandler(sender, discovery);

        var message = CreateMessage("Completed", "Completed", "Namespace.SomeOtherTrain");
        await handler.HandleAsync(message, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Event Payload Mapping

    [Test]
    public async Task HandleAsync_MapsFieldsCorrectly()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage("Completed", "Completed", "My.Train");

        await handler.HandleAsync(message, CancellationToken.None);

        var evt = sender.Events[0].Message as TrainLifecycleEvent;
        evt.Should().NotBeNull();
        evt!.MetadataId.Should().Be(42);
        evt.ExternalId.Should().Be("ext-123");
        evt.TrainName.Should().Be("My.Train");
        evt.TrainState.Should().Be(TrainState.Completed);
        evt.Timestamp.Should().Be(new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task HandleAsync_MapsFailureDetailsCorrectly()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage(
            "Failed",
            "Failed",
            "My.Train",
            failureStep: "ProcessData",
            failureReason: "NullReferenceException"
        );

        await handler.HandleAsync(message, CancellationToken.None);

        var evt = sender.Events[0].Message as TrainLifecycleEvent;
        evt!.FailureStep.Should().Be("ProcessData");
        evt.FailureReason.Should().Be("NullReferenceException");
    }

    [Test]
    public async Task HandleAsync_InvalidTrainState_DefaultsToPending()
    {
        var sender = new RecordingTopicEventSender();
        var handler = CreateHandler(sender, enabledTrainName: "My.Train");
        var message = CreateMessage("Completed", "InvalidState", "My.Train");

        await handler.HandleAsync(message, CancellationToken.None);

        var evt = sender.Events[0].Message as TrainLifecycleEvent;
        evt!.TrainState.Should().Be(TrainState.Pending);
    }

    #endregion

    #region No Enabled Trains

    [Test]
    public async Task NoEnabledTrains_AllEventsSkipped()
    {
        var sender = new RecordingTopicEventSender();
        var discovery = new StubDiscoveryService([]);
        var handler = new GraphQLTrainEventHandler(sender, discovery);

        await handler.HandleAsync(
            CreateMessage("Started", "InProgress", "Any.Train"),
            CancellationToken.None
        );
        await handler.HandleAsync(
            CreateMessage("Completed", "Completed", "Any.Train"),
            CancellationToken.None
        );
        await handler.HandleAsync(
            CreateMessage("Failed", "Failed", "Any.Train"),
            CancellationToken.None
        );
        await handler.HandleAsync(
            CreateMessage("Cancelled", "Cancelled", "Any.Train"),
            CancellationToken.None
        );

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Multiple Enabled Trains

    [Test]
    public async Task MultipleEnabledTrains_OnlyMatchingTrainForwards()
    {
        var sender = new RecordingTopicEventSender();
        var registrations = new[]
        {
            CreateRegistration("First.Train", broadcastEnabled: true),
            CreateRegistration("Second.Train", broadcastEnabled: true),
            CreateRegistration("Third.Train", broadcastEnabled: false),
        };
        var discovery = new StubDiscoveryService(registrations);
        var handler = new GraphQLTrainEventHandler(sender, discovery);

        await handler.HandleAsync(
            CreateMessage("Completed", "Completed", "First.Train"),
            CancellationToken.None
        );
        await handler.HandleAsync(
            CreateMessage("Completed", "Completed", "Second.Train"),
            CancellationToken.None
        );
        await handler.HandleAsync(
            CreateMessage("Completed", "Completed", "Third.Train"),
            CancellationToken.None
        );

        sender.Events.Should().HaveCount(2);
    }

    #endregion

    #region Test Helpers

    private static GraphQLTrainEventHandler CreateHandler(
        RecordingTopicEventSender sender,
        string enabledTrainName
    )
    {
        var registrations = new[] { CreateRegistration(enabledTrainName, broadcastEnabled: true) };
        var discovery = new StubDiscoveryService(registrations);
        return new GraphQLTrainEventHandler(sender, discovery);
    }

    private static TrainLifecycleEventMessage CreateMessage(
        string eventType,
        string trainState,
        string trainName,
        string? failureStep = null,
        string? failureReason = null
    ) =>
        new(
            MetadataId: 42,
            ExternalId: "ext-123",
            TrainName: trainName,
            TrainState: trainState,
            Timestamp: new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc),
            FailureStep: failureStep,
            FailureReason: failureReason,
            EventType: eventType,
            Executor: "RemoteWorker"
        );

    private static TrainRegistration CreateRegistrationWithDistinctTypes(
        string serviceTypeName,
        string implementationTypeName,
        bool broadcastEnabled
    )
    {
        return new TrainRegistration
        {
            ServiceType = new FakeType(serviceTypeName),
            ImplementationType = new FakeType(implementationTypeName),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = serviceTypeName,
            ImplementationTypeName = implementationTypeName,
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = broadcastEnabled,
            GraphQLOperations = GraphQLOperation.Run,
        };
    }

    private static TrainRegistration CreateRegistration(string fullName, bool broadcastEnabled)
    {
        // The handler matches on ServiceType.FullName (the canonical interface name).
        return new TrainRegistration
        {
            ServiceType = new FakeType(fullName),
            ImplementationType = new FakeType(fullName),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = fullName,
            ImplementationTypeName = fullName,
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = broadcastEnabled,
            GraphQLOperations = GraphQLOperation.Run,
        };
    }

    /// <summary>
    /// Minimal Type subclass that returns a controlled FullName for testing the HashSet lookup.
    /// </summary>
    private class FakeType : Type
    {
        private readonly string _fullName;

        public FakeType(string fullName)
        {
            _fullName = fullName;
        }

        public override string? FullName => _fullName;
        public override string Name => _fullName;

        // Required abstract members — not used in the handler
        public override Assembly Assembly => throw new NotImplementedException();
        public override string? AssemblyQualifiedName => _fullName;
        public override Type? BaseType => null;
        public override Guid GUID => Guid.Empty;
        public override Module Module => throw new NotImplementedException();
        public override string? Namespace => null;
        public override Type UnderlyingSystemType => this;

        public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr) => [];

        public override object[] GetCustomAttributes(bool inherit) => [];

        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];

        public override Type? GetElementType() => null;

        public override EventInfo? GetEvent(string name, BindingFlags bindingAttr) => null;

        public override EventInfo[] GetEvents(BindingFlags bindingAttr) => [];

        public override FieldInfo? GetField(string name, BindingFlags bindingAttr) => null;

        public override FieldInfo[] GetFields(BindingFlags bindingAttr) => [];

        public override Type? GetInterface(string name, bool ignoreCase) => null;

        public override Type[] GetInterfaces() => [];

        public override MemberInfo[] GetMembers(BindingFlags bindingAttr) => [];

        public override MethodInfo[] GetMethods(BindingFlags bindingAttr) => [];

        public override Type? GetNestedType(string name, BindingFlags bindingAttr) => null;

        public override Type[] GetNestedTypes(BindingFlags bindingAttr) => [];

        public override PropertyInfo[] GetProperties(BindingFlags bindingAttr) => [];

        public override object? InvokeMember(
            string name,
            BindingFlags invokeAttr,
            Binder? binder,
            object? target,
            object?[]? args,
            ParameterModifier[]? modifiers,
            CultureInfo? culture,
            string[]? namedParameters
        ) => null;

        public override bool IsDefined(Type attributeType, bool inherit) => false;

        protected override TypeAttributes GetAttributeFlagsImpl() => TypeAttributes.Public;

        protected override ConstructorInfo? GetConstructorImpl(
            BindingFlags bindingAttr,
            Binder? binder,
            CallingConventions callConvention,
            Type[] types,
            ParameterModifier[]? modifiers
        ) => null;

        protected override MethodInfo? GetMethodImpl(
            string name,
            BindingFlags bindingAttr,
            Binder? binder,
            CallingConventions callConvention,
            Type[]? types,
            ParameterModifier[]? modifiers
        ) => null;

        protected override PropertyInfo? GetPropertyImpl(
            string name,
            BindingFlags bindingAttr,
            Binder? binder,
            Type? returnType,
            Type[]? types,
            ParameterModifier[]? modifiers
        ) => null;

        protected override bool HasElementTypeImpl() => false;

        protected override bool IsArrayImpl() => false;

        protected override bool IsByRefImpl() => false;

        protected override bool IsCOMObjectImpl() => false;

        protected override bool IsPointerImpl() => false;

        protected override bool IsPrimitiveImpl() => false;
    }

    private class StubDiscoveryService : ITrainDiscoveryService
    {
        private readonly IReadOnlyList<TrainRegistration> _registrations;

        public StubDiscoveryService(IReadOnlyList<TrainRegistration> registrations)
        {
            _registrations = registrations;
        }

        public IReadOnlyList<TrainRegistration> DiscoverTrains() => _registrations;
    }

    public record RecordedEvent(string Topic, object Message);

    private class RecordingTopicEventSender : ITopicEventSender
    {
        public List<RecordedEvent> Events { get; } = [];

        public ValueTask SendAsync<TMessage>(
            string topicName,
            TMessage message,
            CancellationToken cancellationToken = default
        )
        {
            Events.Add(new RecordedEvent(topicName, message!));
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(string topicName) => ValueTask.CompletedTask;
    }

    #endregion
}
