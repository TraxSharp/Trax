using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Provider.Json.Services.JsonEffect;

namespace Trax.Effect.Tests.Json.Integration.IntegrationTests;

public class JsonEffectProviderLogStateTests
{
    [Test]
    public async Task SaveChanges_LogsTheMaskedStringNotTheModel()
    {
        var logger = new StateCapturingLogger();
        var provider = new JsonEffectProvider(
            logger,
            new TraxEffectConfiguration { LogLevel = LogLevel.Information }
        );
        var model = Metadata.Create(
            new CreateMetadata
            {
                Name = "LoggedAsString",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await provider.Track(model);
        model.Output = "changed";
        await provider.SaveChanges(CancellationToken.None);

        logger.States.Should().ContainSingle();
        var state = logger.States[0];
        state.Should().NotBeSameAs(model);
        var values = state
            .Should()
            .BeAssignableTo<IReadOnlyList<KeyValuePair<string, object?>>>()
            .Subject;
        values.Should().NotContain(kv => kv.Key.StartsWith('@'));
        values.Should().NotContain(kv => ReferenceEquals(kv.Value, model));
        values
            .Single(kv => kv.Key == "Metadata")
            .Value.Should()
            .BeOfType<string>()
            .Which.Should()
            .Contain("changed");
    }

    /// <summary>Records the state object of every entry, so a test can see what a structured sink is handed.</summary>
    private sealed class StateCapturingLogger : ILogger<JsonEffectProvider>
    {
        public List<object?> States { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => States.Add(state);
    }
}
