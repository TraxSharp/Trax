using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;

namespace Trax.Effect.Tests.Json.Integration.Fixtures;

public abstract class TestSetup
{
    private ServiceProvider ServiceProvider { get; set; }

    public IServiceScope Scope { get; private set; }

    private ServiceCollection ServiceCollection { get; set; }

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        ServiceCollection = new ServiceCollection();

        var arrayProvider = new ArrayLoggingProvider();

        ServiceCollection
            .AddSingleton<IArrayLoggingProvider>(arrayProvider)
            .AddLogging(x =>
                x.AddConsole().AddProvider(arrayProvider).SetMinimumLevel(LogLevel.Debug)
            )
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects
                        .SetEffectLogLevel(LogLevel.Information)
                        .AddJson()
                        .AddStepLogger(serializeStepData: true)
                )
            );

        ServiceProvider = ConfigureServices(ServiceCollection);
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests()
    {
        await ServiceProvider.DisposeAsync();
    }

    [SetUp]
    public virtual async Task TestSetUp()
    {
        Scope = ServiceProvider.CreateScope();
    }

    [TearDown]
    public async Task TestTearDown()
    {
        Scope.Dispose();
    }

    public abstract ServiceProvider ConfigureServices(IServiceCollection services);
}
