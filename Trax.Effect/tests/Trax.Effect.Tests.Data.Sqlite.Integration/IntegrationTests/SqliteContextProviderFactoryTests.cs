using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Services.SqliteContextFactory;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteContextProviderFactoryTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    [Test]
    public void Create_IncrementsCount()
    {
        var factory = (SqliteContextProviderFactory)
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var before = factory.Count;

        using var ctx1 = factory.Create();
        using var ctx2 = factory.Create();

        factory.Count.Should().Be(before + 2);
    }

    [Test]
    public async Task CreateDbContextAsync_ReturnsContextAndIncrementsCount()
    {
        var factory = (SqliteContextProviderFactory)
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var before = factory.Count;

        await using var ctx = await factory.CreateDbContextAsync(CancellationToken.None);

        ctx.Should().NotBeNull();
        factory.Count.Should().Be(before + 1);
    }
}
