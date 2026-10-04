using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// An entity that is not a <c>[TraxQueryModel]</c> declares no authorization posture. When a
/// query model has a navigation to it, HotChocolate infers an object type for it with every
/// public property, so the host refuses to start until the entity declares whether that surface
/// is public, before any request could read or filter on it.
/// </summary>
[TestFixture]
public class QueryModelUndeclaredNavigationTests
{
    private const string Secret = "tok_live_0123456789";

    private const string Adr =
        "Trax.Api docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md";

    [Test]
    public async Task A_navigation_to_an_undeclared_entity_refuses_startup_before_it_can_be_read()
    {
        var act = () => StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(typeof(UndeclaredAccount).FullName!, Adr)
            .And.Contain("'UndeclaredPost.account'", "the object field reads the entity");
    }

    [Test]
    public async Task A_navigation_to_an_undeclared_entity_refuses_startup_before_it_can_be_filtered_or_sorted()
    {
        var act = () => StartAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("'UndeclaredPostFilterInput.account'", Adr)
            .And.Contain("'UndeclaredPostSortInput.account'", Adr);
    }

    private static async Task<(ServiceProvider, IRequestExecutor)> StartAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        var root = new InMemoryDatabaseRoot();
        var name = "UndeclaredNav_" + Guid.NewGuid();
        services.AddDbContext<UndeclaredNavDbContext>(o => o.UseInMemoryDatabase(name, root));

        services.AddTraxGraphQL(g => g.AddDbContext<UndeclaredNavDbContext>());

        var provider = services.BuildServiceProvider();

        // Every startup validator Trax registered runs, as it would in a host.
        try
        {
            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StartAsync(default);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UndeclaredNavDbContext>();
            var account = new UndeclaredAccount
            {
                Id = 1,
                Email = "ceo@example.com",
                ApiToken = Secret,
            };
            db.Accounts.Add(account);
            db.Posts.Add(
                new UndeclaredPost
                {
                    Id = 1,
                    Title = "hello",
                    AccountId = 1,
                    Account = account,
                }
            );
            await db.SaveChangesAsync();
        }

        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return (provider, executor);
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class UndeclaredPost
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public long AccountId { get; set; }
        public UndeclaredAccount? Account { get; set; }
    }

    // Not a query model: no posture of its own.
    public class UndeclaredAccount
    {
        public long Id { get; set; }
        public string Email { get; set; } = "";
        public string ApiToken { get; set; } = "";
    }

    public class UndeclaredNavDbContext(DbContextOptions<UndeclaredNavDbContext> options)
        : DbContext(options)
    {
        public DbSet<UndeclaredPost> Posts { get; set; } = null!;
        public DbSet<UndeclaredAccount> Accounts { get; set; } = null!;
    }
}
