using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.JunctionEffectProviderFactory;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// Every run asks each registered factory for a fresh provider and disposes it when the run ends.
/// A factory that hands the provider out of the root container, or keeps a list of what it made,
/// holds every run's provider (and the run's tracked models) for the life of the process.
/// </summary>
[TestFixture]
public class EffectProviderFactoryRetentionTests
{
    private const int Runs = 1000;

    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UseInMemory()
                    .AddJson()
                    .SaveTrainParameters()
                    .AddJunctionLogger()
                    .AddJunctionProgress()
            )
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public void EffectProviderFactories_DisposedProviders_AreNotRetained()
    {
        var factories = _provider.GetServices<IEffectProviderFactory>().ToList();
        factories.Should().HaveCountGreaterThanOrEqualTo(2);

        var retained = factories.ToDictionary(
            f => f.GetType().Name,
            f => CountRetained(() => f.Create())
        );

        retained
            .Should()
            .OnlyContain(r => r.Value == 0, "no factory may keep the providers it created");
    }

    [Test]
    public void JunctionEffectProviderFactories_DisposedProviders_AreNotRetained()
    {
        var factories = _provider.GetServices<IJunctionEffectProviderFactory>().ToList();
        factories.Should().HaveCount(3);

        var retained = factories.ToDictionary(
            f => f.GetType().Name,
            f => CountRetained(() => f.Create())
        );

        retained
            .Should()
            .OnlyContain(r => r.Value == 0, "no factory may keep the providers it created");
    }

    private static int CountRetained(Func<IDisposable> create)
    {
        var references = CreateAndDispose(create, Runs);
        CollectGarbage();
        return references.Count(r => r.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> CreateAndDispose(Func<IDisposable> create, int count)
    {
        var references = new List<WeakReference>(count);
        for (var i = 0; i < count; i++)
        {
            var created = create();
            references.Add(new WeakReference(created));
            created.Dispose();
        }

        return references;
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
