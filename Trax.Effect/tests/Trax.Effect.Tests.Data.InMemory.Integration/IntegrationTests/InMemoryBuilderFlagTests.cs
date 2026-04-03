using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

[TestFixture]
public class InMemoryBuilderFlagTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _provider = new ServiceCollection()
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task TearDown() => await _provider.DisposeAsync();

    [Test]
    public void UseInMemory_DoesNotRegister_ISqlDialect()
    {
        var dialect = _provider.GetService<ISqlDialect>();
        dialect.Should().BeNull();
    }

    [Test]
    public void UseInMemory_RegistersIDataContext()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetService<IDataContext>();
        context.Should().NotBeNull();
    }

    [Test]
    public void UseInMemory_RegistersIDataContextProviderFactory()
    {
        var factory = _provider.GetService<IDataContextProviderFactory>();
        factory.Should().NotBeNull();
    }
}
