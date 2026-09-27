using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// Discovery reports whether a train overrides <c>QueueSubjectKey</c>, with the same check the
/// enqueue uses to decide whether to ask it for a key, so a surface such as the dashboard can tell
/// a subject-keyed train from one that is never serialized.
/// </summary>
[TestFixture]
public class TrainDiscoveryQueueSubjectKeyTests
{
    private static TrainRegistration Discover<TService, TImpl>()
        where TImpl : class, TService
        where TService : class
    {
        var services = new ServiceCollection();
        services.AddScoped<TService, TImpl>();
        return new TrainDiscoveryService(services)
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(TService));
    }

    [Test]
    public void A_train_that_overrides_QueueSubjectKey_is_reported_as_keyed()
    {
        Discover<IKeyedTrain, KeyedTrain>().HasQueueSubjectKey.Should().BeTrue();
    }

    [Test]
    public void A_train_that_does_not_override_QueueSubjectKey_is_reported_as_unkeyed()
    {
        Discover<IUnkeyedTrain, UnkeyedTrain>()
            .HasQueueSubjectKey.Should()
            .BeFalse("the base returns null for every train, so it is never serialized");
    }

    [Test]
    public void An_override_inherited_from_a_base_class_counts()
    {
        Discover<IInheritedKeyTrain, InheritedKeyTrain>().HasQueueSubjectKey.Should().BeTrue();
    }

    [Test]
    public void Overriding_only_OnQueue_does_not_make_a_train_keyed()
    {
        Discover<IHookOnlyTrain, HookOnlyTrain>().HasQueueSubjectKey.Should().BeFalse();
    }

    // ─── Fakes ───────────────────────────────────────────────

    public record KeyedIn;

    public record UnkeyedIn;

    public record InheritedIn;

    public record HookOnlyIn;

    public interface IKeyedTrain : IServiceTrain<KeyedIn, Unit>;

    public class KeyedTrain : ServiceTrain<KeyedIn, Unit>, IKeyedTrain
    {
        protected override string? QueueSubjectKey(Metadata metadata) => "subject";

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IUnkeyedTrain : IServiceTrain<UnkeyedIn, Unit>;

    public class UnkeyedTrain : ServiceTrain<UnkeyedIn, Unit>, IUnkeyedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public abstract class KeyedBase<TIn> : ServiceTrain<TIn, Unit>
    {
        protected override string? QueueSubjectKey(Metadata metadata) => "inherited";
    }

    public interface IInheritedKeyTrain : IServiceTrain<InheritedIn, Unit>;

    public class InheritedKeyTrain : KeyedBase<InheritedIn>, IInheritedKeyTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IHookOnlyTrain : IServiceTrain<HookOnlyIn, Unit>;

    public class HookOnlyTrain : ServiceTrain<HookOnlyIn, Unit>, IHookOnlyTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            Task.CompletedTask;

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
