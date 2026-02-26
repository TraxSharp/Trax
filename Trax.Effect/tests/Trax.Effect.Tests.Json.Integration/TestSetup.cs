using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Tests.Json.Integration;

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
            .AddLogging(
                x => x.AddConsole().AddProvider(arrayProvider).SetMinimumLevel(LogLevel.Debug)
            )
            .AddTrax.CoreEffects(
                options =>
                    options
                        .SetEffectLogLevel(LogLevel.Information)
                        .AddJsonEffect()
                        .AddStepLogger(serializeStepData: true)
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
