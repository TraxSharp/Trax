using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.Auth;
using Trax.Core.Exceptions;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Exceptions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Api.Tests.OperationsFailureMaskingTests;

namespace Trax.Api.Tests;

/// <summary>
/// One rule decides which train failure message a client reads, whichever surface ran the train:
/// an exact <see cref="TrainException"/>'s message is shown, a subclass's is not. The typed train
/// field goes through the error filter and <c>operations.workQueue.queueTrain</c> through the
/// operations service's refusal; both answer the same. And no mapped error carries its exception
/// into the response, even with HotChocolate's exception details switched on.
///
/// <para>Enforces <c>docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md")]
public class TrainExceptionMessageRuleTests
{
    private const string Adr =
        "docs/adr/0014-only-a-train-exceptions-own-message-reaches-the-client.md";

    private const string TrainName = "Trax.Api.Tests.OperationsFailureMaskingTests+IMaskedTrain";

    [Test]
    public async Task A_TrainExceptions_message_is_shown_on_the_typed_field_and_on_queueTrain()
    {
        var (typed, operations) = await QueueOnBothSurfaces(
            new TrainException("Order 42 is already closed.")
        );

        typed.Should().Be("Order 42 is already closed.");
        operations.Should().Be("The enqueue was refused: Order 42 is already closed.");
    }

    [Test]
    public async Task A_subclass_message_is_hidden_on_the_typed_field_and_on_queueTrain()
    {
        var (typed, operations) = await QueueOnBothSurfaces(
            new LedgerLockedException("Ledger 7 is locked by batch 0x2f on db-primary.")
        );

        typed.Should().Be("The train failed.", Adr);
        operations.Should().Be("The enqueue was refused.", Adr);
    }

    [Test]
    public async Task TrainAlreadyStarted_is_hidden_on_the_typed_field_and_on_queueTrain()
    {
        var (typed, operations) = await QueueOnBothSurfaces(
            new TrainAlreadyStartedException(4242, TrainName)
        );

        typed.Should().Be("The train failed.", Adr);
        operations.Should().Be("The enqueue was refused.", Adr);
    }

    private static IEnumerable<TestCaseData> MappedFailures()
    {
        yield return new TestCaseData(
            new TrainException("Order 42 is already closed."),
            "TRAX_TRAIN_ERROR"
        ).SetArgDisplayNames(nameof(TrainException));
        yield return new TestCaseData(
            new LedgerLockedException("Ledger 7 is locked"),
            "TRAX_TRAIN_ERROR"
        ).SetArgDisplayNames("TrainExceptionSubclass");
        yield return new TestCaseData(
            new RemoteRunException("Remote run endpoint returned HTTP 502: upstream 10.0.3.7"),
            "TRAX_TRAIN_ERROR"
        ).SetArgDisplayNames(nameof(RemoteRunException));
        yield return new TestCaseData(
            new TrainAuthorizationException(TrainName, "Missing role: Admin"),
            "TRAX_AUTHORIZATION"
        ).SetArgDisplayNames(nameof(TrainAuthorizationException));
        yield return new TestCaseData(
            new TrainNotFoundException(TrainName),
            "TRAX_TRAIN_NOT_FOUND"
        ).SetArgDisplayNames(nameof(TrainNotFoundException));
        yield return new TestCaseData(
            new AmbiguousTrainNameException("IMaskedTrain", ["Ns.A.IMaskedTrain", TrainName]),
            "TRAX_AMBIGUOUS_TRAIN"
        ).SetArgDisplayNames(nameof(AmbiguousTrainNameException));
        yield return new TestCaseData(
            new TrainInputValidationException("IMaskedTrain", 2048, 1024),
            "TRAX_INVALID_INPUT"
        ).SetArgDisplayNames(nameof(TrainInputValidationException));
    }

    [TestCaseSource(nameof(MappedFailures))]
    public async Task A_mapped_error_has_no_exception_detail_when_details_are_on(
        Exception thrown,
        string code
    )
    {
        var execution = Substitute.For<ITrainExecutionService>();
        execution
            .RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(thrown);

        await using var services = BuildDetailedServices(execution);
        var executor = await services
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = await executor.ExecuteAsync(
            "mutation { dispatch { masked(input: {}) { __typename } } }"
        );

        var body = result.ExpectOperationResult().ToJson();
        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("errors")[0];
        var extensions = error.GetProperty("extensions");
        extensions.GetProperty("code").GetString().Should().Be(code);
        extensions
            .TryGetProperty("exception", out _)
            .Should()
            .BeFalse("a mapped error's public shape is the whole of what the client reads");
        body.Should().NotContain("stackTrace").And.NotContain(thrown.GetType().Name);
    }

    [Test]
    public async Task An_unmapped_error_keeps_HotChocolates_exception_detail_when_details_are_on()
    {
        var execution = Substitute.For<ITrainExecutionService>();
        execution
            .RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ArgumentException("development detail"));

        await using var services = BuildDetailedServices(execution);
        var executor = await services
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = await executor.ExecuteAsync(
            "mutation { dispatch { masked(input: {}) { __typename } } }"
        );

        result
            .ExpectOperationResult()
            .ToJson()
            .Should()
            .Contain("development detail", "the host asked for HotChocolate's detail on errors");
    }

    private static async Task<(string? Typed, string? Operations)> QueueOnBothSurfaces(
        Exception refusal
    )
    {
        var execution = Substitute.For<ITrainExecutionService>();
        execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(refusal);

        // A store that is not the in-memory one, so both surfaces attempt the enqueue rather than
        // refuse it as having nothing to dispatch it.
        using var host = await StartHostAsync(
            execution,
            new RecordingSubmitter(),
            QueueRegistration(),
            Substitute.For<IDataContextProviderFactory>()
        );

        using var typed = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            "mutation { dispatch { masked(input: {}) { __typename } } }"
        );
        using var operations = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            $$"""
            mutation {
              operations {
                workQueue {
                  queueTrain(input: { trainName: "{{TrainName}}", inputJson: "{}" }) {
                    success
                    message
                  }
                }
              }
            }
            """
        );

        await host.StopAsync();

        var typedMessage = typed
            .RootElement.GetProperty("errors")[0]
            .GetProperty("message")
            .GetString();
        var payload = operations
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("workQueue")
            .GetProperty("queueTrain");
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        return (typedMessage, payload.GetProperty("message").GetString());
    }

    private static TrainRegistration QueueRegistration() => Typed(GraphQLOperation.Queue);

    private static TrainRegistration RunRegistration() => Typed(GraphQLOperation.Run);

    private static TrainRegistration Typed(GraphQLOperation operations) =>
        new()
        {
            ServiceType = typeof(IMaskedTrain),
            ImplementationType = typeof(MaskedTrain),
            InputType = typeof(MaskedInput),
            OutputType = typeof(Trax.Core.Functional.Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = nameof(IMaskedTrain),
            ImplementationTypeName = nameof(MaskedTrain),
            InputTypeName = nameof(MaskedInput),
            OutputTypeName = nameof(Trax.Core.Functional.Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            HasAllowAnonymousAttribute = true,
            IsQuery = false,
            IsMutation = true,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLName = "masked",
            GraphQLOperations = operations,
        };

    private static ServiceProvider BuildDetailedServices(ITrainExecutionService execution)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([RunRegistration()]);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddTraxGraphQL(graphql =>
            graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        services
            .AddGraphQLServer("trax")
            .ModifyRequestOptions(o => o.IncludeExceptionDetails = true);
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<IOperationsService>());
        services.AddScoped(_ => execution);
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
        return services.BuildServiceProvider();
    }

    private sealed class LedgerLockedException(string message) : TrainException(message);
}
