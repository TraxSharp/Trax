using FluentAssertions;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Startup;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// Whether enforcement is on is invisible in a running system, so it is stated at startup. A host
/// keying enforcement on the environment gets the warning wherever it is off, which is the case the
/// line exists for.
/// </summary>
[TestFixture]
public class PersistedOperationsEnforcementReporterTests
{
    private static async Task<List<(LogLevel Level, string Message)>> ReportAsync(
        bool requirePersisted,
        bool shadowLogging = false
    )
    {
        var builder = new PersistedOperationsBuilder().UseDatabase("Host=fake;Database=fake");
        builder.RequirePersisted(requirePersisted);

        // Enforcement off with no shadow logging is refused by Build(), so the shadow flag comes
        // along whenever the caller asks for enforcement off without it.
        if (!requirePersisted)
            builder.LogNonPersistedRequests(true);

        var options = builder.Build();
        options.LogNonPersistedRequests = shadowLogging || !requirePersisted;

        var recorder = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(b =>
            b.AddProvider(recorder).SetMinimumLevel(LogLevel.Trace)
        );

        await new PersistedOperationsEnforcementReporter(options, factory).StartAsync(
            CancellationToken.None
        );

        return recorder.Entries;
    }

    [Test]
    public async Task EnforcementOn_SaysSoAndSaysItIsNotAnAuthorizationBoundary()
    {
        var entries = await ReportAsync(requirePersisted: true);

        var entry = entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information, "enforcement being on is the ordinary case");
        entry.Message.Should().Contain("enforcement is ON");
        entry
            .Message.Should()
            .Contain(
                "not an authorization boundary",
                "conflating the two is the mistake this line exists to prevent"
            );
    }

    [Test]
    public async Task EnforcementOff_WarnsAndNamesTheRemainingBoundary()
    {
        var entries = await ReportAsync(requirePersisted: false);

        var entry = entries.Should().ContainSingle().Subject;
        entry
            .Level.Should()
            .Be(
                LogLevel.Warning,
                "an endpoint serving any inline query is the state that is easy to be wrong about"
            );
        entry.Message.Should().Contain("enforcement is OFF");
        entry.Message.Should().Contain("[TraxAuthorize] is the only boundary");
        entry
            .Message.Should()
            .Contain(
                "running as Development",
                "a prod-data environment running as Development is the case that motivated this"
            );
    }

    [Test]
    public async Task EnforcementOff_WithShadowLogging_SaysShadowLoggingIsOn()
    {
        var entries = await ReportAsync(requirePersisted: false, shadowLogging: true);

        entries.Should().ContainSingle().Which.Message.Should().Contain("shadow logging on");
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(Entries);

        public void Dispose() { }

        private sealed class Recorder(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
