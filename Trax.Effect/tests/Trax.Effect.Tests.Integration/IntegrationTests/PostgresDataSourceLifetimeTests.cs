using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The <see cref="NpgsqlDataSource"/> <c>UsePostgres</c> builds belongs to the service provider
/// that resolves it, so disposing the provider closes its connection pool. A data source the
/// container does not own stays open until Npgsql prunes idle connections, and a host that builds
/// and discards providers, as a test suite does, runs the server out of connections.
/// </summary>
[NonParallelizable]
public class PostgresDataSourceLifetimeTests
{
    private static readonly string ConnectionString = TestPostgres.WithPort(
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build()
            .GetRequiredSection("Configuration")["DatabaseConnectionString"]!
    );

    [Test]
    public async Task Disposing_the_provider_disposes_its_data_source()
    {
        var provider = BuildProvider(new ServiceCollection());
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        await using (var connection = await dataSource.OpenConnectionAsync())
            connection.State.Should().Be(System.Data.ConnectionState.Open);

        await provider.DisposeAsync();

        var open = async () => await dataSource.OpenConnectionAsync();
        await open.Should()
            .ThrowAsync<ObjectDisposedException>(
                "the container owns the data source, so disposing it closes the pool"
            );
    }

    [Test]
    public async Task Two_providers_from_one_collection_do_not_share_a_data_source()
    {
        var services = new ServiceCollection();
        BuildProvider(services, build: false);
        var first = services.BuildServiceProvider();
        var second = services.BuildServiceProvider();

        // The first provider resolves its data source, so disposing the provider disposes it.
        await (
            await first.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync()
        ).DisposeAsync();
        await first.DisposeAsync();

        await using (second)
        {
            var factory = second.GetRequiredService<IDataContextProviderFactory>();
            using var context = await factory.CreateDbContextAsync(CancellationToken.None);
            var reading = async () =>
                await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                    context.Metadatas
                );
            await reading
                .Should()
                .NotThrowAsync(
                    "disposing one provider must not close the pool another provider is using"
                );
        }
    }

    private static ServiceProvider BuildProvider(IServiceCollection services, bool build = true)
    {
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UsePostgres(ConnectionString)));
        return build ? services.BuildServiceProvider() : null!;
    }
}
