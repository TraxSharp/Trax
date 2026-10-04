using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// A caller's train input is read without regard to property-name case, so the API and the
/// dashboard accept the same JSON, and a property named twice (in any casing) is refused rather
/// than resolved silently to whichever came last.
///
/// <para>Enforces <c>Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md")]
public class TrainInputCasingTests
{
    private const string Adr =
        "Trax.Docs/adr/0023-caller-supplied-train-input-is-read-case-insensitively.md";

    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(TrainInputCasingTests).Assembly])
        );
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        TrainBus.ClearMethodCache();
    }

    private async Task<PaymentInput> QueuedInputAsync(string json)
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var result = await execution.QueueAsync(typeof(IPaymentTrain).FullName!, json);

        var factory = _serviceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var stored = await context
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == result.WorkQueueId);

        return JsonSerializer.Deserialize<PaymentInput>(
            stored.Input!,
            new JsonSerializerOptions(TraxEffectConfiguration.StaticSystemJsonSerializerOptions)
            {
                PropertyNameCaseInsensitive = true,
            }
        )!;
    }

    [TestCase("""{"amount":5,"reference":"r-1"}""")]
    [TestCase("""{"Amount":5,"Reference":"r-1"}""")]
    [TestCase("""{"AMOUNT":5,"rEfErEnCe":"r-1"}""")]
    public async Task Input_properties_are_matched_whatever_their_case(string json)
    {
        var input = await QueuedInputAsync(json);

        input
            .Should()
            .Be(
                new PaymentInput { Amount = 5, Reference = "r-1" },
                $"a mis-cased property used to be dropped and the train queued with its default ({Adr})"
            );
    }

    [TestCase("""{"amount":1,"Amount":999}""")]
    [TestCase("""{"amount":1,"amount":999}""")]
    public async Task A_property_given_twice_is_refused_at_enqueue(string json)
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var queue = async () => await execution.QueueAsync(typeof(IPaymentTrain).FullName!, json);
        var run = async () => await execution.RunAsync(typeof(IPaymentTrain).FullName!, json);

        await queue
            .Should()
            .ThrowAsync<JsonException>(
                $"which value the caller meant is ambiguous, so neither is taken ({Adr})"
            )
            .WithMessage("*Duplicate property*");
        await run.Should().ThrowAsync<JsonException>("RunAsync reads input the same way");

        var factory = _serviceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync()).Should().Be(0, "a refused input writes nothing");
    }

    public record PaymentInput
    {
        public int Amount { get; init; }
        public string Reference { get; init; } = "";
    }

    public interface IPaymentTrain : IServiceTrain<PaymentInput, Unit>;

    public class PaymentTrain : ServiceTrain<PaymentInput, Unit>, IPaymentTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
