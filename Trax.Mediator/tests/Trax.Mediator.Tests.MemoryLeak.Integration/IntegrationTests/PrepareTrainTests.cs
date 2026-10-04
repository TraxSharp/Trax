using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// <see cref="ITrainExecutionService.PrepareAsync"/> is the one place a caller's train name and
/// input become a registration and an input instance: the lookup, the authorization check, and
/// the input reading that <c>QueueAsync</c> and <c>RunAsync</c> also go through. A surface that
/// submits work some other way (the scheduler's run operation) uses it instead of a copy.
/// </summary>
[TestFixture]
public class PrepareTrainTests
{
    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(m =>
                    m.ScanAssemblies(typeof(PrepareTrainTests).Assembly).WithMaxInputJsonBytes(256)
                )
        );
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        TrainBus.ClearMethodCache();
    }

    private ITrainExecutionService Execution(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    [Test]
    public async Task It_returns_the_registration_and_the_input_read_as_a_queue_reads_it()
    {
        using var scope = _serviceProvider.CreateScope();

        var prepared = await Execution(scope)
            .PrepareAsync(typeof(IPrepareTrain).FullName!, """{"Label":"x","Count":3}""");

        prepared.Registration.ServiceType.Should().Be(typeof(IPrepareTrain));
        prepared
            .Input.Should()
            .Be(new PrepareInput { Label = "x", Count = 3 }, "casing is ignored, as ADR 0023 says");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task A_blank_input_is_read_as_an_empty_object(string? json)
    {
        using var scope = _serviceProvider.CreateScope();

        var prepared = await Execution(scope).PrepareAsync(typeof(IPrepareTrain).FullName!, json);

        prepared.Input.Should().Be(new PrepareInput());
    }

    [Test]
    public async Task A_json_null_a_duplicate_and_an_oversized_input_are_refused()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = Execution(scope);
        var name = typeof(IPrepareTrain).FullName!;

        await ((Func<Task>)(() => execution.PrepareAsync(name, "null")))
            .Should()
            .ThrowAsync<JsonException>()
            .WithMessage("*deserialized to null*");
        await ((Func<Task>)(() => execution.PrepareAsync(name, """{"count":1,"Count":2}""")))
            .Should()
            .ThrowAsync<JsonException>()
            .WithMessage("*Duplicate property*");
        await (
            (Func<Task>)(
                () => execution.PrepareAsync(name, $$"""{"label":"{{new string('x', 300)}}"}""")
            )
        )
            .Should()
            .ThrowAsync<TrainInputValidationException>();
    }

    [Test]
    public async Task An_unknown_train_is_refused_as_QueueAsync_refuses_it()
    {
        using var scope = _serviceProvider.CreateScope();

        await ((Func<Task>)(() => Execution(scope).PrepareAsync("No.Such.Train", "{}")))
            .Should()
            .ThrowAsync<TrainNotFoundException>();
    }

    [Test]
    public async Task Authorization_runs_before_the_input_is_read()
    {
        using var scope = _serviceProvider.CreateScope();

        // Invalid JSON for a gated train on a host with no enforcer: the refusal is the
        // authorization one, so a caller who may not use the train learns nothing about its input.
        await (
            (Func<Task>)(
                () =>
                    Execution(scope).PrepareAsync(typeof(IGatedPrepareTrain).FullName!, "{not json")
            )
        )
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*declares [TraxAuthorize] but no ITrainAuthorizationService*");
    }

    [Test]
    public async Task A_trusted_scope_passes_authorization_as_it_does_for_a_queue()
    {
        using var scope = _serviceProvider.CreateScope();
        var trusted = scope.ServiceProvider.GetRequiredService<ITrustedExecutionScope>();

        using (trusted.BeginTrusted("test.prepare"))
        {
            var prepared = await Execution(scope)
                .PrepareAsync(typeof(IGatedPrepareTrain).FullName!, """{"label":"ok"}""");

            prepared.Input.Should().Be(new GatedPrepareInput { Label = "ok" });
        }
    }

    [Test]
    public void A_prepared_train_can_only_come_from_PrepareAsync()
    {
        typeof(PreparedTrain)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .BeEmpty(
                "a caller that could build one could hand a submitter an input nobody authorized"
            );
        typeof(PreparedTrain).IsSealed.Should().BeTrue("a subclass could expose a constructor");
    }

    [Test]
    public async Task An_implementation_that_predates_PrepareAsync_refuses_it_rather_than_skipping_it()
    {
        ITrainExecutionService legacy = Substitute.ForPartsOf<LegacyExecution>();

        await ((Func<Task>)(() => legacy.PrepareAsync("any", "{}")))
            .Should()
            .ThrowAsync<NotSupportedException>();
    }

    public abstract class LegacyExecution : ITrainExecutionService
    {
        public abstract Task<QueueTrainResult> QueueAsync(
            string trainName,
            string? inputJson,
            int priority = 0,
            DateTime? scheduledAt = null,
            CancellationToken ct = default
        );

        public abstract Task<RunTrainResult> RunAsync(
            string trainName,
            string inputJson,
            CancellationToken ct = default
        );
    }

    public record PrepareInput
    {
        public string Label { get; init; } = "";
        public int Count { get; init; }
    }

    public interface IPrepareTrain : IServiceTrain<PrepareInput, Unit>;

    public class PrepareTrain : ServiceTrain<PrepareInput, Unit>, IPrepareTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record GatedPrepareInput
    {
        public string Label { get; init; } = "";
    }

    [TraxAuthorize("Admin")]
    public interface IGatedPrepareTrain : IServiceTrain<GatedPrepareInput, Unit>;

    public class GatedPrepareTrain : ServiceTrain<GatedPrepareInput, Unit>, IGatedPrepareTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
