using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextLoggingProvider;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A line a run writes through <c>ILogger</c> is stored against that run, so the log table can be
/// read run by run. A line written outside any run names none.
/// </summary>
[TestFixture]
[NonParallelizable]
public class LogRunLinkTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var connectionString = TestPostgres.WithPort(
            configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        );

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UsePostgres(connectionString)
                    .AddDataContextLogging(minimumLogLevel: LogLevel.Information)
            )
        );
        services
            .AddScopedTraxRoute<IOuterTrain, OuterTrain>()
            .AddScopedTraxRoute<IInnerTrain, InnerTrain>()
            .AddScopedTraxRoute<IProviderBuildingTrain, ProviderBuildingTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task A_line_logged_in_a_run_names_that_run_and_one_logged_outside_names_none()
    {
        var tag = Guid.NewGuid().ToString("N");

        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOuterTrain>().Run(tag);
        }

        _provider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(Category)
            .LogInformation("{Tag} outside", tag);

        var rows = await WaitForLines(tag, count: 4);

        var outerId = _outerRunId!.Value;
        var innerId = _innerRunId!.Value;
        outerId.Should().BePositive();
        innerId.Should().NotBe(outerId);

        rows[$"{tag} outer before"].Should().Be(outerId);
        rows[$"{tag} inner"]
            .Should()
            .Be(innerId, "a train run inside a junction names its own run");
        rows[$"{tag} outer after"]
            .Should()
            .Be(outerId, "the outer run's lines name it again once the inner run returns");
        rows[$"{tag} outside"].Should().Be(0, "no run was going when it was written");
    }

    [Test]
    public async Task A_logging_provider_built_inside_a_run_does_not_store_its_writers_lines_against_it()
    {
        var tag = Guid.NewGuid().ToString("N");

        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProviderBuildingTrain>().Run(tag);
        }

        // Disposing drains the writer, so the line it logged itself is stored by now.
        _builtInRun!.Dispose();

        var rows = await WaitForLines(tag, count: 2);

        _buildingRunId.Should().BePositive();
        rows[$"{tag} in the run"].Should().Be(_buildingRunId!.Value);
        rows[$"{tag} from the writer"]
            .Should()
            .Be(0, "the writer is not part of the run that happened to build its provider");
    }

    private const string Category = "Audit.LogRunLinkTests";

    private static long? _buildingRunId;
    private static DataContextLoggingProvider? _builtInRun;

    private static long? _outerRunId;
    private static long? _innerRunId;

    private async Task<Dictionary<string, long>> WaitForLines(string tag, int count)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();

        // Polled rather than waited on: the writer flushes on its own schedule.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            using var context = (IDataContext)factory.Create();
            var rows = await context
                .Logs.AsNoTracking()
                .Where(l => l.Category == Category && l.Message.StartsWith(tag))
                .ToDictionaryAsync(l => l.Message, l => l.MetadataId);

            if (rows.Count >= count || DateTime.UtcNow > deadline)
                return rows;

            await Task.Yield();
        }
    }

    public class OuterJunction(ILoggerFactory loggers, IInnerTrain inner)
        : EffectJunction<string, Unit>
    {
        public override async Task<Unit> Run(string tag)
        {
            _outerRunId = Metadata!.TrainMetadataId;
            var logger = loggers.CreateLogger(Category);

            logger.LogInformation("{Tag} outer before", tag);
            await inner.Run(tag);
            logger.LogInformation("{Tag} outer after", tag);

            return Unit.Default;
        }
    }

    public class InnerJunction(ILoggerFactory loggers) : EffectJunction<string, Unit>
    {
        public override Task<Unit> Run(string tag)
        {
            _innerRunId = Metadata!.TrainMetadataId;
            loggers.CreateLogger(Category).LogInformation("{Tag} inner", tag);
            return Task.FromResult(Unit.Default);
        }
    }

    /// <summary>
    /// Builds a logging provider on the run's flow, as the first logger a run asks for does, over a
    /// factory that logs a line through that provider when the writer opens its data context, so
    /// the line is logged on the writer's own flow.
    /// </summary>
    public class ProviderBuildingJunction(IDataContextProviderFactory factory)
        : EffectJunction<string, Unit>
    {
        public override Task<Unit> Run(string tag)
        {
            _buildingRunId = Metadata!.TrainMetadataId;

            var writerFactory = new LoggingOnOpenFactory(factory, tag);
            var provider = new DataContextLoggingProvider(
                writerFactory,
                new DataContextLoggingProviderConfiguration
                {
                    MinimumLogLevel = LogLevel.Information,
                }
            );
            writerFactory.Provider = provider;
            _builtInRun = provider;

            provider.CreateLogger(Category).LogInformation("{Tag} in the run", tag);
            return Task.FromResult(Unit.Default);
        }
    }

    private sealed class LoggingOnOpenFactory(IDataContextProviderFactory inner, string tag)
        : IDataContextProviderFactory
    {
        public DataContextLoggingProvider? Provider { get; set; }

        public IEffectProvider Create() => inner.Create();

        public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
        {
            // Wait until the junction has handed over the provider; the writer starts in its
            // constructor, before the assignment.
            while (Provider is null)
                await Task.Yield();

            Provider.CreateLogger(Category).LogInformation("{Tag} from the writer", tag);
            return await inner.CreateDbContextAsync(cancellationToken);
        }
    }

    public interface IProviderBuildingTrain : IServiceTrain<string, Unit>;

    public class ProviderBuildingTrain : ServiceTrain<string, Unit>, IProviderBuildingTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ProviderBuildingJunction>().Resolve();
    }

    public interface IOuterTrain : IServiceTrain<string, Unit>;

    public class OuterTrain : ServiceTrain<string, Unit>, IOuterTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<OuterJunction>().Resolve();
    }

    public interface IInnerTrain : IServiceTrain<string, Unit>;

    public class InnerTrain : ServiceTrain<string, Unit>, IInnerTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<InnerJunction>().Resolve();
    }
}
