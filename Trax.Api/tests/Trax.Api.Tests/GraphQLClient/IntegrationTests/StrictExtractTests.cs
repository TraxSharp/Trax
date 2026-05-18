using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Strict-extract is the kernel's answer to "I added a field to the query but forgot to add
/// it to the POCO." Every test here would let a real drift bug ship if deleted: silent
/// behavior under Lenient is desired, a logged warning under WarnOnDrift is the production
/// signal, and an exception under ThrowOnDrift is the dev/test signal.
/// </summary>
[TestFixture]
public class StrictExtractTests
{
    private GraphQLTestServerFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new GraphQLTestServerFixture();

    [TearDown]
    public void TearDown() => _fixture.Dispose();

    private IGraphQLClientExecutor BuildExecutor(
        ResponseStrictness strictness,
        ILogger<GraphQLClientExecutor>? logger = null
    )
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(_fixture.BaseAddress)
            .ConfigureHttpClient(_fixture.CreateHttpClient())
            .WithStrictness(strictness);
        if (logger is not null)
        {
            services.AddSingleton(logger);
        }
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IGraphQLClientExecutor>();
    }

    [Test]
    public async Task Lenient_PocoMissingResponseField_SucceedsSilently()
    {
        var executor = BuildExecutor(ResponseStrictness.Lenient);

        // PlayerNameOnly POCO has Id + Name; query selects id, name, level. `level` is extra
        // in the response. Lenient must ignore it.
        var result = await executor.Run(new GetPlayerNameOnlyRequest { Id = "player-1" });

        result.Id.Should().Be("player-1");
        result.Name.Should().Be("Aragorn");
    }

    [Test]
    public async Task ThrowOnDrift_PocoMissingResponseField_ThrowsShapeException()
    {
        var executor = BuildExecutor(ResponseStrictness.ThrowOnDrift);

        var act = async () => await executor.Run(new GetPlayerNameOnlyRequest { Id = "player-1" });

        var ex = await act.Should().ThrowAsync<GraphQLResponseShapeException>();
        ex.Which.ExtraJsonFields.Should().Contain("level");
        ex.Which.TargetType.Should().Be(typeof(PlayerNameOnly));
    }

    [Test]
    public async Task WarnOnDrift_PocoMissingResponseField_LogsWarningButReturnsValue()
    {
        var captured = new CapturedLogger();
        var executor = BuildExecutor(ResponseStrictness.WarnOnDrift, captured);

        var result = await executor.Run(new GetPlayerNameOnlyRequest { Id = "player-1" });

        result.Name.Should().Be("Aragorn");
        captured
            .Entries.Should()
            .ContainSingle(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains("drift")
                && e.Message.Contains("level")
            );
    }

    [Test]
    public async Task ThrowOnDrift_PocoMatchesResponse_DoesNotThrow()
    {
        var executor = BuildExecutor(ResponseStrictness.ThrowOnDrift);

        // GetPlayerByRawStringRequest's POCO matches the query exactly; no drift.
        var result = await executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });

        result.Name.Should().Be("Aragorn");
    }

    private sealed class CapturedLogger : ILogger<GraphQLClientExecutor>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
