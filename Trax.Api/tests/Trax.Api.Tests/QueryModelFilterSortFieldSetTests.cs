using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
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
/// A query model's <c>where</c> and <c>order</c> inputs offer exactly the fields its type
/// exposes. <c>BindFields = Explicit</c> and <c>ExposeAs</c> narrow the type, so they narrow the
/// filter and sort inputs with it, both on the model's own entry field and wherever another
/// model's input reaches it through a navigation. Guard for
/// <c>docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md</c>.
/// </summary>
[Property("adr", "docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md")]
[TestFixture]
public class QueryModelFilterSortFieldSetTests
{
    private const string Adr =
        "docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md: filter and sort "
        + "follow the exposed field set";

    [Test]
    public async Task Explicit_binding_leaves_a_non_column_property_out_of_the_filter_input()
    {
        var (provider, executor) = await StartAsync();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { fieldSetAccounts(where: { passwordHash: { startsWith: \"secret\" } }) { nodes { name } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("alice", Adr);
    }

    [Test]
    public async Task Explicit_binding_leaves_a_non_column_property_out_of_the_sort_input()
    {
        var (provider, executor) = await StartAsync();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { fieldSetAccounts(order: [{ passwordHash: ASC }]) { nodes { name } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("alice", Adr);
    }

    [Test]
    public async Task Explicit_binding_still_filters_and_sorts_on_a_column()
    {
        var (provider, executor) = await StartAsync();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { fieldSetAccounts(where: { name: { eq: \"alice\" } }, order: [{ name: ASC }]) { nodes { name } } } }"
        );

        json.Should().NotContain("\"errors\"").And.Contain("alice");
    }

    [Test]
    public async Task A_navigation_to_an_explicitly_bound_model_filters_only_on_its_columns()
    {
        var (provider, executor) = await StartAsync();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { fieldSetTickets(where: { account: { passwordHash: { startsWith: \"secret\" } } }) { nodes { subject } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("printer", Adr);
    }

    [Test]
    public async Task A_navigation_to_an_explicitly_bound_model_sorts_only_on_its_columns()
    {
        var (provider, executor) = await StartAsync();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { fieldSetTickets(order: [{ account: { passwordHash: ASC } }]) { nodes { subject } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("printer", Adr);
    }

    [Test]
    public async Task A_navigation_to_an_exposed_as_model_filters_only_on_the_interface()
    {
        var (provider, executor) = await StartAsync();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { fieldSetTickets(where: { assignee: { pinCode: { eq: \"1234\" } } }) { nodes { subject } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("printer", Adr);
    }

    [Test]
    public async Task A_navigation_to_a_restricted_model_still_filters_on_an_exposed_field()
    {
        var (provider, executor) = await StartAsync();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { fieldSetTickets(where: { account: { name: { eq: \"alice\" } }, assignee: { handle: { eq: \"bob\" } } }) { nodes { subject } } } }"
        );

        json.Should().NotContain("\"errors\"").And.Contain("printer");
    }

    private static async Task<string> RunAsync(IRequestExecutor executor, string query) =>
        (await executor.ExecuteAsync(query)).ExpectOperationResult().ToJson();

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
        var name = "FieldSet_" + Guid.NewGuid();
        services.AddDbContext<FieldSetDbContext>(o => o.UseInMemoryDatabase(name, root));

        services.AddTraxGraphQL(g => g.AddDbContext<FieldSetDbContext>());

        var provider = services.BuildServiceProvider();

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(default);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FieldSetDbContext>();
            var account = new FieldSetAccount
            {
                Id = 1,
                Name = "alice",
                PasswordHash = "secret-hash",
            };
            var assignee = new FieldSetAssignee
            {
                Id = 1,
                Handle = "bob",
                PinCode = "1234",
            };
            db.Accounts.Add(account);
            db.Assignees.Add(assignee);
            db.Tickets.Add(
                new FieldSetTicket
                {
                    Id = 1,
                    Subject = "printer",
                    AccountId = 1,
                    Account = account,
                    AssigneeId = 1,
                    Assignee = assignee,
                }
            );
            await db.SaveChangesAsync();
        }

        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return (provider, executor);
    }

    [TraxAllowAnonymous]
    [TraxQueryModel(BindFields = FieldBindingBehavior.Explicit)]
    public class FieldSetAccount
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        public string PasswordHash { get; set; } = "";
    }

    public interface IFieldSetAssignee
    {
        long Id { get; }
        string Handle { get; }
    }

    [TraxAllowAnonymous]
    [TraxQueryModel(ExposeAs = typeof(IFieldSetAssignee))]
    public class FieldSetAssignee : IFieldSetAssignee
    {
        public long Id { get; set; }
        public string Handle { get; set; } = "";
        public string PinCode { get; set; } = "";
    }

    [TraxAllowAnonymous]
    [TraxQueryModel]
    public class FieldSetTicket
    {
        public long Id { get; set; }
        public string Subject { get; set; } = "";
        public long AccountId { get; set; }
        public FieldSetAccount? Account { get; set; }
        public long AssigneeId { get; set; }
        public FieldSetAssignee? Assignee { get; set; }
    }

    public class FieldSetDbContext(DbContextOptions<FieldSetDbContext> options) : DbContext(options)
    {
        public DbSet<FieldSetAccount> Accounts { get; set; } = null!;
        public DbSet<FieldSetAssignee> Assignees { get; set; } = null!;
        public DbSet<FieldSetTicket> Tickets { get; set; } = null!;
    }
}
