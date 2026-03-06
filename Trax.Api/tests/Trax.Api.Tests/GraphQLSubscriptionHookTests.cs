using System.Globalization;
using System.Reflection;
using FluentAssertions;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

[TestFixture]
public class GraphQLSubscriptionHookTests
{
    #region Attribute Gating — OnStarted

    [Test]
    public async Task OnStarted_EnabledTrain_PublishesEvent()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("Enabled.Train");

        await hook.OnStarted(metadata, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainStarted));
    }

    [Test]
    public async Task OnStarted_DisabledTrain_SkipsPublishing()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("NotEnabled.Train");

        await hook.OnStarted(metadata, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Attribute Gating — OnCompleted

    [Test]
    public async Task OnCompleted_EnabledTrain_PublishesEvent()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("Enabled.Train");

        await hook.OnCompleted(metadata, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCompleted));
    }

    [Test]
    public async Task OnCompleted_DisabledTrain_SkipsPublishing()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("Other.Train");

        await hook.OnCompleted(metadata, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Attribute Gating — OnFailed

    [Test]
    public async Task OnFailed_EnabledTrain_PublishesEvent()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("Enabled.Train");

        await hook.OnFailed(metadata, new Exception("fail"), CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainFailed));
    }

    [Test]
    public async Task OnFailed_DisabledTrain_SkipsPublishing()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("Other.Train");

        await hook.OnFailed(metadata, new Exception("fail"), CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Attribute Gating — OnCancelled

    [Test]
    public async Task OnCancelled_EnabledTrain_PublishesEvent()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("Enabled.Train");

        await hook.OnCancelled(metadata, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCancelled));
    }

    [Test]
    public async Task OnCancelled_DisabledTrain_SkipsPublishing()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "Enabled.Train");
        var metadata = CreateMetadata("Other.Train");

        await hook.OnCancelled(metadata, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Event Payload Mapping

    [Test]
    public async Task OnCompleted_MapsMetadataFieldsCorrectly()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "My.Train");
        var metadata = CreateMetadata("My.Train");
        metadata.TrainState = TrainState.Completed;
        metadata.EndTime = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);

        await hook.OnCompleted(metadata, CancellationToken.None);

        var evt = sender.Events[0].Message as TrainLifecycleEvent;
        evt.Should().NotBeNull();
        evt!.TrainName.Should().Be("My.Train");
        evt.TrainState.Should().Be(TrainState.Completed);
        evt.MetadataId.Should().Be(metadata.Id);
        evt.ExternalId.Should().Be(metadata.ExternalId);
        evt.Timestamp.Should().Be(metadata.EndTime.Value);
    }

    [Test]
    public async Task OnFailed_MapsFailureFieldsCorrectly()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "My.Train");
        var metadata = CreateMetadata("My.Train");
        metadata.AddException(new InvalidOperationException("something broke"));

        await hook.OnFailed(
            metadata,
            new InvalidOperationException("something broke"),
            CancellationToken.None
        );

        var evt = sender.Events[0].Message as TrainLifecycleEvent;
        evt.Should().NotBeNull();
        evt!.FailureReason.Should().Contain("something broke");
    }

    [Test]
    public async Task OnStarted_NoEndTime_UsesCurrentUtcTime()
    {
        var sender = new RecordingTopicEventSender();
        var hook = CreateHook(sender, enabledTrainName: "My.Train");
        var metadata = CreateMetadata("My.Train");
        metadata.EndTime = null;

        var before = DateTime.UtcNow;
        await hook.OnStarted(metadata, CancellationToken.None);
        var after = DateTime.UtcNow;

        var evt = sender.Events[0].Message as TrainLifecycleEvent;
        evt!.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    #endregion

    #region Service vs Implementation Type Name Matching

    [Test]
    public async Task OnCompleted_MetadataNameMatchesServiceType_PublishesEvent()
    {
        // GraphQL mutations set metadata.Name = registration.ServiceType.FullName
        // (the interface name, e.g. "IGenerateSustainabilityReportTrain")
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            subscriptionEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var hook = new GraphQLSubscriptionHook(sender, discovery);

        var metadata = CreateMetadata("Namespace.IMyTrain");
        await hook.OnCompleted(metadata, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCompleted));
    }

    [Test]
    public async Task OnCompleted_MetadataNameMatchesImplementationType_PublishesEvent()
    {
        // Direct train execution sets metadata.Name = GetType().FullName
        // (the concrete class name, e.g. "GenerateSustainabilityReportTrain")
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            subscriptionEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var hook = new GraphQLSubscriptionHook(sender, discovery);

        var metadata = CreateMetadata("Namespace.MyTrain");
        await hook.OnCompleted(metadata, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCompleted));
    }

    [Test]
    public async Task OnCompleted_MetadataNameMatchesNeitherType_SkipsPublishing()
    {
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            subscriptionEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var hook = new GraphQLSubscriptionHook(sender, discovery);

        var metadata = CreateMetadata("Namespace.SomeOtherTrain");
        await hook.OnCompleted(metadata, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    [Test]
    public async Task OnStarted_MetadataNameMatchesServiceType_PublishesEvent()
    {
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            subscriptionEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var hook = new GraphQLSubscriptionHook(sender, discovery);

        var metadata = CreateMetadata("Namespace.IMyTrain");
        await hook.OnStarted(metadata, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainStarted));
    }

    [Test]
    public async Task OnFailed_MetadataNameMatchesServiceType_PublishesEvent()
    {
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            subscriptionEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var hook = new GraphQLSubscriptionHook(sender, discovery);

        var metadata = CreateMetadata("Namespace.IMyTrain");
        await hook.OnFailed(metadata, new Exception("fail"), CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainFailed));
    }

    [Test]
    public async Task OnCancelled_MetadataNameMatchesServiceType_PublishesEvent()
    {
        var sender = new RecordingTopicEventSender();
        var registration = CreateRegistrationWithDistinctTypes(
            serviceTypeName: "Namespace.IMyTrain",
            implementationTypeName: "Namespace.MyTrain",
            subscriptionEnabled: true
        );
        var discovery = new StubDiscoveryService([registration]);
        var hook = new GraphQLSubscriptionHook(sender, discovery);

        var metadata = CreateMetadata("Namespace.IMyTrain");
        await hook.OnCancelled(metadata, CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnTrainCancelled));
    }

    #endregion

    #region No Enabled Trains

    [Test]
    public async Task NoEnabledTrains_AllEventsSkipped()
    {
        var sender = new RecordingTopicEventSender();
        var discovery = new StubDiscoveryService([]);
        var hook = new GraphQLSubscriptionHook(sender, discovery);
        var metadata = CreateMetadata("Any.Train");

        await hook.OnStarted(metadata, CancellationToken.None);
        await hook.OnCompleted(metadata, CancellationToken.None);
        await hook.OnFailed(metadata, new Exception(), CancellationToken.None);
        await hook.OnCancelled(metadata, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region Multiple Enabled Trains

    [Test]
    public async Task MultipleEnabledTrains_OnlyMatchingTrainPublishes()
    {
        var sender = new RecordingTopicEventSender();
        var registrations = new[]
        {
            CreateRegistration("First.Train", subscriptionEnabled: true),
            CreateRegistration("Second.Train", subscriptionEnabled: true),
            CreateRegistration("Third.Train", subscriptionEnabled: false),
        };
        var discovery = new StubDiscoveryService(registrations);
        var hook = new GraphQLSubscriptionHook(sender, discovery);

        await hook.OnCompleted(CreateMetadata("First.Train"), CancellationToken.None);
        await hook.OnCompleted(CreateMetadata("Second.Train"), CancellationToken.None);
        await hook.OnCompleted(CreateMetadata("Third.Train"), CancellationToken.None);

        sender.Events.Should().HaveCount(2);
    }

    #endregion

    #region Factory

    [Test]
    public void GraphQLSubscriptionHookFactory_CreatesHookFromDI()
    {
        var services = new ServiceCollection();
        var sender = new RecordingTopicEventSender();
        var discovery = new StubDiscoveryService([]);

        services.AddSingleton<ITopicEventSender>(sender);
        services.AddSingleton<ITrainDiscoveryService>(discovery);
        services.AddTransient<GraphQLSubscriptionHook>();
        using var provider = services.BuildServiceProvider();

        var factory = new GraphQLSubscriptionHookFactory(provider);
        var hook = factory.Create();

        hook.Should().NotBeNull();
        hook.Should().BeOfType<GraphQLSubscriptionHook>();
    }

    #endregion

    #region Test Helpers

    private static GraphQLSubscriptionHook CreateHook(
        RecordingTopicEventSender sender,
        string enabledTrainName
    )
    {
        var registrations = new[]
        {
            CreateRegistration(enabledTrainName, subscriptionEnabled: true),
        };
        var discovery = new StubDiscoveryService(registrations);
        return new GraphQLSubscriptionHook(sender, discovery);
    }

    private static Metadata CreateMetadata(string name)
    {
        return new Metadata
        {
            Name = name,
            ExternalId = Guid.NewGuid().ToString("N"),
            TrainState = TrainState.InProgress,
        };
    }

    private static TrainRegistration CreateRegistrationWithDistinctTypes(
        string serviceTypeName,
        string implementationTypeName,
        bool subscriptionEnabled
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
            IsBroadcastEnabled = subscriptionEnabled,
            GraphQLOperations = GraphQLOperation.Run,
        };
    }

    private static TrainRegistration CreateRegistration(string fullName, bool subscriptionEnabled)
    {
        // Create a stub type that reports the given FullName
        // Since we can't fake Type.FullName, we use a real type and the hook
        // matches on ImplementationType.FullName. We work around this by creating
        // a StubDiscoveryService that returns registrations with the matching FullName.
        return new TrainRegistration
        {
            ServiceType = typeof(object),
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
            IsBroadcastEnabled = subscriptionEnabled,
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

        // Required abstract members — not used in the hook
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
            System.Globalization.CultureInfo? culture,
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
