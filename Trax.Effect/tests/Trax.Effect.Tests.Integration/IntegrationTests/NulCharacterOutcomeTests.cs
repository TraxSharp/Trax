using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// Postgres refuses a NUL character: a <c>text</c> column refuses <c>\0</c> and a <c>jsonb</c>
/// column refuses the <c>\u0000</c> escape. A run whose output or failure message carries one must
/// still record its outcome, rather than being left <c>InProgress</c> for the reaper to fail and a
/// manifest to re-run.
/// </summary>
[TestFixture]
[NonParallelizable]
public class NulCharacterOutcomeTests
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
            trax.AddEffects(effects => effects.UsePostgres(connectionString).SaveTrainParameters())
        );
        services
            .AddScopedTraxRoute<INulOutputTrain, NulOutputTrain>()
            .AddScopedTraxRoute<INulFailureTrain, NulFailureTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task A_NUL_in_the_output_is_recorded_Completed()
    {
        using var scope = _provider.CreateScope();
        var train = (NulOutputTrain)scope.ServiceProvider.GetRequiredService<INulOutputTrain>();

        var output = await train.Run(Unit.Default);

        output.Should().Be("bytes:\0end", "the train's caller gets the output it produced");
        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Completed);
        row.EndTime.Should().NotBeNull();
        row.Output.Should().NotBeNull().And.NotContain("\\u0000");
    }

    [Test]
    public async Task A_NUL_in_the_exception_message_is_recorded_Failed_with_an_end_time()
    {
        using var scope = _provider.CreateScope();
        var train = (NulFailureTrain)scope.ServiceProvider.GetRequiredService<INulFailureTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<FormatException>();

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Failed);
        row.EndTime.Should().NotBeNull();
        row.FailureReason.Should().NotBeNull().And.NotContain("\0");
    }

    [Test]
    public async Task A_NUL_written_through_a_synchronous_save_is_replaced()
    {
        using var scope = _provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var category = $"nul-sync-{Guid.NewGuid():N}";

        using (var context = (IDataContext)factory.Create())
        {
            context.Logs.Add(
                new Log
                {
                    Level = LogLevel.Warning,
                    Message = "bytes:\0end",
                    Category = category,
                }
            );
            ((DbContext)context).SaveChanges();
        }

        using var reader = (IDataContext)factory.Create();
        var stored = await reader.Logs.AsNoTracking().SingleAsync(l => l.Category == category);
        stored.Message.Should().Be("bytes:\uFFFDend");
    }

    private static async Task<Metadata> PersistedRow(IServiceScope scope, long id)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    public interface INulOutputTrain : IServiceTrain<Unit, string>;

    public class NulOutputTrain : ServiceTrain<Unit, string>, INulOutputTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<ReturnsNulJunction>().Resolve();

        public class ReturnsNulJunction : EffectJunction<Unit, string>
        {
            public override Task<string> Run(Unit input) => Task.FromResult("bytes:\0end");
        }
    }

    public interface INulFailureTrain : IServiceTrain<Unit, Unit>;

    public class NulFailureTrain : ServiceTrain<Unit, Unit>, INulFailureTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ParsesNulJunction>().Resolve();

        public class ParsesNulJunction : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input)
            {
                // The runtime's message quotes the input, NUL and all.
                _ = int.Parse("12\u00003");
                return Task.FromResult(input);
            }
        }
    }
}
