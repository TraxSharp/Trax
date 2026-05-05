using System.Reflection;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class CancellationCheckProviderTests
{
    private static IDataContextProviderFactory BuildInMemoryFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IDataContextProviderFactory>();
    }

    [Test]
    public async Task BeforeJunctionExecution_NullMetadata_ReturnsWithoutThrow()
    {
        var factory = BuildInMemoryFactory();
        var provider = new CancellationCheckProvider(factory);
        var train = new TestTrain();
        var junction = new TestEffectJunction();

        // train.Metadata is null — should be a no-op.
        Func<Task> act = async () =>
            await provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task BeforeJunctionExecution_MetadataNotInDb_ReturnsWithoutThrow()
    {
        var factory = BuildInMemoryFactory();
        var provider = new CancellationCheckProvider(factory);
        var train = new TestTrain();
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "T",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        SetTrainMetadata(train, meta);
        var junction = new TestEffectJunction();

        // Metadata not persisted to DB — query returns default(false) → no throw.
        Func<Task> act = async () =>
            await provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task AfterJunctionExecution_AlwaysCompletedSuccessfully()
    {
        var factory = BuildInMemoryFactory();
        var provider = new CancellationCheckProvider(factory);

        await provider.AfterJunctionExecution(
            new TestEffectJunction(),
            new TestTrain(),
            CancellationToken.None
        );
    }

    [Test]
    public void Dispose_DoesNotThrow()
    {
        var factory = BuildInMemoryFactory();
        var provider = new CancellationCheckProvider(factory);

        Action act = () => provider.Dispose();
        act.Should().NotThrow();
    }

    private static void SetTrainMetadata(TestTrain train, Metadata metadata) =>
        typeof(ServiceTrain<string, string>)
            .GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(train, metadata);

    private class TestTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Task.FromResult<Either<Exception, string>>(input);
    }

    private class TestEffectJunction : EffectJunction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }
}
