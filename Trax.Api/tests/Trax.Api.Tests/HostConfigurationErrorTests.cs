using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A run the host is not built to serve (no registered train takes the input) is a host
/// configuration error. The caller gets a generic message and the <c>TRAX_HOST_CONFIGURATION</c>
/// code, even with exception details switched on; the input type and the scanned assemblies go to
/// the server log only.
/// </summary>
[TestFixture]
public class HostConfigurationErrorTests
{
    private const string ScannedAssembly = "Contoso.Billing.Trains";

    [Test]
    public async Task RunMutation_NoTrainTakesTheInput_ReturnsTheGenericMessageAndLogsTheDetail()
    {
        var logs = new RecordingLoggerProvider();
        var execution = Substitute.For<ITrainExecutionService>();
        execution
            .RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NoTrainForInputException(typeof(OrphanInput), [ScannedAssembly]));

        await using var services = BuildServices(logs, execution);
        var executor = await services
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = await executor.ExecuteAsync(
            """mutation { dispatch { orphan(input: { name: "x" }) { metadataId } } }"""
        );

        var body = result.ExpectOperationResult().ToJson();
        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("errors")[0];
        error.GetProperty("message").GetString().Should().Be("The train could not be run.");
        error
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("TRAX_HOST_CONFIGURATION");

        body.Should()
            .NotContain(ScannedAssembly, "the host's assembly list is for its log, not a caller")
            .And.NotContain(nameof(OrphanInput))
            .And.NotContain("stackTrace");

        logs.Messages.Should()
            .Contain(m => m.Contains(nameof(OrphanInput)) && m.Contains(ScannedAssembly));
    }

    private static ServiceProvider BuildServices(
        RecordingLoggerProvider logs,
        ITrainExecutionService execution
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([OrphanRegistration()]);
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

    private static TrainRegistration OrphanRegistration() =>
        new()
        {
            ServiceType = typeof(IOrphanTrain),
            ImplementationType = typeof(OrphanTrain),
            InputType = typeof(OrphanInput),
            OutputType = typeof(Trax.Core.Functional.Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(IOrphanTrain).FullName!,
            ImplementationTypeName = nameof(OrphanTrain),
            InputTypeName = nameof(OrphanInput),
            OutputTypeName = nameof(Trax.Core.Functional.Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = true,
            IsBroadcastEnabled = false,
            HasAllowAnonymousAttribute = true,
            GraphQLName = "orphan",
            GraphQLOperations = GraphQLOperation.Run,
            IsRemote = false,
        };

    private interface IOrphanTrain;

    private sealed class OrphanTrain;

    public sealed record OrphanInput
    {
        public string Name { get; init; } = "";
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose() { }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (IsEnabled(logLevel))
                    lock (messages)
                        messages.Add(formatter(state, exception) + " " + exception?.Message);
            }
        }
    }
}
