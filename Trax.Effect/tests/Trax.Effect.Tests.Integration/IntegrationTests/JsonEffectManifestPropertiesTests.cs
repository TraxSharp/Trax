using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The JSON effect logs a run's metadata, which deliberately leaves the input out
/// (<c>Metadata.Input</c> is <c>[JsonIgnore]</c>). A scheduled run's metadata arrives with its
/// manifest loaded, and the manifest's properties are that same input, unmasked.
/// </summary>
public class JsonEffectManifestPropertiesTests
{
    private const string Secret = "sk-live-hunter2";
    private readonly CapturingLoggerProvider _logs = new();
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .SetEffectLogLevel(LogLevel.Information)
                    .UseInMemory()
                    .AddJson()
                    .AddJunctionLogger(serializeJunctionData: true)
            )
        );
        services.AddScopedTraxRoute<IChargeTrain, ChargeTrain>();
        services.AddScopedTraxRoute<ILoadQueueTrain, LoadQueueTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task A_scheduled_runs_manifest_properties_are_not_written_to_the_log()
    {
        using var scope = _provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var charge = new ChargeInput { Customer = "ada", ApiKey = Secret };

        long metadataId;
        using (var seed = (IDataContext)factory.Create())
        {
            var group = new ManifestGroup { Name = "billing" };
            seed.ManifestGroups.Add(group);
            await seed.SaveChanges(CancellationToken.None);

            var manifest = Manifest.Create(
                new CreateManifest { Name = typeof(IChargeTrain), Properties = charge }
            );
            manifest.ManifestGroupId = group.Id;
            seed.Manifests.Add(manifest);
            await seed.SaveChanges(CancellationToken.None);

            var pending = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(IChargeTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    ManifestId = manifest.Id,
                }
            );
            seed.Metadatas.Add(pending);
            await seed.SaveChanges(CancellationToken.None);
            metadataId = pending.Id;
        }

        // What the scheduler's job runner does: load the row with its manifest, then run the
        // train under it.
        using var load = (IDataContext)factory.Create();
        var metadata = await load
            .Metadatas.Include(m => m.Manifest)
            .SingleAsync(m => m.Id == metadataId);
        var train = (ChargeTrain)scope.ServiceProvider.GetRequiredService<IChargeTrain>();
        _logs.Lines.Clear();

        await train.Run(charge, metadata);

        _logs
            .Lines.Should()
            .NotContain(
                line => line.Contains(Secret),
                "the JSON effect keeps a run's input out of its log line, and the manifest's "
                    + "properties are the input (with [TraxSensitive] members unmasked)"
            );
    }

    [Test]
    public async Task A_junction_that_loads_queued_entries_does_not_log_their_inputs()
    {
        // The scheduler's own dispatcher is a train whose junction returns the queued entries,
        // and AddJunctionLogger(serializeJunctionData: true) logs each junction's output.
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ILoadQueueTrain>();
        _logs.Lines.Clear();

        await train.Run(Unit.Default);

        _logs
            .Lines.Should()
            .NotContain(
                line => line.Contains(Secret),
                "a queued entry's input is the real value the train runs with, [TraxSensitive] "
                    + "members included; a junction output that carries it must not log it"
            );
    }

    public sealed class ChargeInput : IManifestProperties
    {
        public string Customer { get; set; } = "";

        [TraxSensitive]
        public string ApiKey { get; set; } = "";
    }

    private class ChargeTrain : ServiceTrain<ChargeInput, Unit>, IChargeTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IChargeTrain : IServiceTrain<ChargeInput, Unit> { }

    public class LoadQueuedJunction : EffectJunction<Unit, List<WorkQueue>>
    {
        public override Task<List<WorkQueue>> Run(Unit input) =>
            Task.FromResult(
                new List<WorkQueue>
                {
                    WorkQueue.Create(
                        new CreateWorkQueue
                        {
                            TrainName = typeof(IChargeTrain).FullName!,
                            Input = $$"""{"customer":"ada","apiKey":"{{Secret}}"}""",
                            InputTypeName = typeof(ChargeInput).FullName,
                        }
                    ),
                }
            );
    }

    private class LoadQueueTrain : ServiceTrain<Unit, List<WorkQueue>>, ILoadQueueTrain
    {
        protected override Task<Either<Exception, List<WorkQueue>>> Junctions() =>
            Chain<LoadQueuedJunction>().Resolve();
    }

    public interface ILoadQueueTrain : IServiceTrain<Unit, List<WorkQueue>> { }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capturing(Lines);

        public void Dispose() { }

        private sealed class Capturing(ConcurrentQueue<string> lines) : ILogger
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
            ) => lines.Enqueue(formatter(state, exception));
        }
    }
}
