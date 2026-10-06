using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.PersistedOperations.GraphQL;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// <see cref="IPersistedOperationsService"/> is the one path the GraphQL fields and a dashboard
/// take, so the same input is refused, or accepted, the same way from either.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PersistedOperationsServiceTests
{
    private ServiceProvider _sp = null!;
    private IPersistedOperationsService _service = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping integration tests.");

        _sp = await GraphQLFixture.BuildAsync();
        _service = _sp.GetRequiredService<IPersistedOperationsService>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_sp is not null)
            await _sp.DisposeAsync();
    }

    [SetUp]
    public Task SetUp() => PostgresFixture.ClearAsync();

    [Test]
    public async Task Upload_Deactivate_Restore_RoundTrip_ThroughTheService()
    {
        var uploaded = await _service.UploadAsync(
            new UploadPersistedOperationInput("svc_v1", GraphQLFixture.ValidDocument),
            CancellationToken.None
        );
        uploaded.Success.Should().BeTrue();

        var deactivated = await _service.DeactivateAsync(
            new DeactivatePersistedOperationInput("svc_v1", "retired"),
            CancellationToken.None
        );
        deactivated.Success.Should().BeTrue();
        (await _service.GetAsync("svc_v1", null, CancellationToken.None))!
            .IsActive.Should()
            .BeFalse("a deactivated operation is still readable");

        var restored = await _service.RestoreAsync(
            new RestorePersistedOperationInput("svc_v1"),
            CancellationToken.None
        );
        restored.Success.Should().BeTrue();

        var history = await _service.GetHistoryAsync("svc_v1", null, 0, 50, CancellationToken.None);
        history.Select(h => h.ChangeType).Should().Equal("Restore", "Deactivate", "Upsert");

        var page = await _service.ListAsync(null, 0, 50, CancellationToken.None);
        page.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task TheServiceAndTheKeptResolverOverloads_RefuseTheSameUpload()
    {
        var input = new UploadPersistedOperationInput(
            "two_ops",
            "query A { hello } query B { version }"
        );

        var viaService = await _service.UploadAsync(input, CancellationToken.None);
        var viaResolver = await new PersistedOperationMutations().UploadPersistedOperation(
            input,
            _sp.GetRequiredService<IPersistedOperationStore>(),
            CancellationToken.None
        );

        viaService.Success.Should().BeFalse();
        viaResolver
            .Errors.Select(e => e.Code)
            .Should()
            .Equal(viaService.Errors.Select(e => e.Code));
        viaService.Errors[0].Code.Should().Be("INVALID_INPUT");
    }

    [Test]
    public async Task Deactivate_UnknownId_IsNotFound()
    {
        var payload = await _service.DeactivateAsync(
            new DeactivatePersistedOperationInput("missing", "gone"),
            CancellationToken.None
        );

        payload.Success.Should().BeFalse();
        payload.Errors[0].Code.Should().Be("NOT_FOUND");
    }

    [Test]
    public async Task TheKeptQueryOverloads_ReadWhatTheServiceReads()
    {
        await _service.UploadAsync(
            new UploadPersistedOperationInput("svc_read", GraphQLFixture.ValidDocument),
            CancellationToken.None
        );
        var factory = _sp.GetRequiredService<IDataContextProviderFactory>();

        var viaResolver = await new PersistedOperationQueries().PersistedOperation(
            "svc_read",
            factory,
            CancellationToken.None
        );
        var viaService = await _service.GetAsync("svc_read", null, CancellationToken.None);

        viaResolver.Should().BeEquivalentTo(viaService);
    }

    [Test]
    public async Task DeactivatingAnInactiveOperation_Succeeds_AndSendsTheChangeAgain()
    {
        await _service.UploadAsync(
            new UploadPersistedOperationInput("svc_twice", GraphQLFixture.ValidDocument),
            CancellationToken.None
        );
        await _service.DeactivateAsync(
            new DeactivatePersistedOperationInput("svc_twice", "retired"),
            CancellationToken.None
        );
        var generation = _sp.GetRequiredService<PersistedOperationCacheGeneration>();
        var before = generation.Current;

        var again = await _service.DeactivateAsync(
            new DeactivatePersistedOperationInput("svc_twice", "retired, sent again"),
            CancellationToken.None
        );

        again.Success.Should().BeTrue("deactivating is idempotent");
        again.Operation!.IsActive.Should().BeFalse();
        again.Operation.DeprecationReason.Should().Be("retired, sent again");
        generation.Current.Should().BeGreaterThan(before, "the caches are emptied again");
    }

    [Test]
    public async Task TheKeptListAndHistoryOverloads_ReadWhatTheServiceReads()
    {
        await _service.UploadAsync(
            new UploadPersistedOperationInput("svc_list", GraphQLFixture.ValidDocument),
            CancellationToken.None
        );
        var factory = _sp.GetRequiredService<IDataContextProviderFactory>();
        var queries = new PersistedOperationQueries();

        (await queries.PersistedOperations(factory, CancellationToken.None))
            .Should()
            .BeEquivalentTo(await _service.ListAsync(null, 0, 50, CancellationToken.None));
        (await queries.PersistedOperationHistory("svc_list", factory, CancellationToken.None))
            .Should()
            .BeEquivalentTo(
                await _service.GetHistoryAsync("svc_list", null, 0, 50, CancellationToken.None)
            );
    }

    [Test]
    public async Task TheKeptDeactivateAndRestoreOverloads_ChangeTheOperation()
    {
        var store = _sp.GetRequiredService<IPersistedOperationStore>();
        var factory = _sp.GetRequiredService<IDataContextProviderFactory>();
        await store.UpsertAsync(
            "svc_kept",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );
        var mutations = new PersistedOperationMutations();

        var deactivated = await mutations.DeactivatePersistedOperation(
            new DeactivatePersistedOperationInput("svc_kept", "retired"),
            store,
            CancellationToken.None
        );
        var restored = await mutations.RestorePersistedOperation(
            new RestorePersistedOperationInput("svc_kept"),
            store,
            factory,
            CancellationToken.None
        );

        deactivated.Success.Should().BeTrue();
        restored.Success.Should().BeTrue();
        (await store.GetAsync("svc_kept", null, CancellationToken.None)).Should().NotBeNull();
    }

    [Test]
    public async Task List_FiltersByTenant_AndClampsThePage()
    {
        await _service.UploadAsync(
            new UploadPersistedOperationInput("svc_t_none", GraphQLFixture.ValidDocument),
            CancellationToken.None
        );
        await _service.UploadAsync(
            new UploadPersistedOperationInput(
                "svc_t_acme",
                GraphQLFixture.ValidDocument,
                TenantKey: "acme"
            ),
            CancellationToken.None
        );

        var noTenant = await _service.ListAsync(
            new PersistedOperationFilter(TenantKey: ""),
            -5,
            0,
            CancellationToken.None
        );
        var acme = await _service.ListAsync(
            new PersistedOperationFilter(TenantKey: "acme", IdStartsWith: "svc_"),
            0,
            10_000,
            CancellationToken.None
        );

        noTenant.Items.Select(i => i.Id).Should().Equal("svc_t_none");
        acme.Items.Select(i => i.Id).Should().Equal("svc_t_acme");
    }

    [Test]
    public async Task AServiceBuiltForOneJob_RefusesTheOther()
    {
        var store = _sp.GetRequiredService<IPersistedOperationStore>();
        var factory = _sp.GetRequiredService<IDataContextProviderFactory>();

        var readWithAStoreOnly = () =>
            PersistedOperationsService
                .ForStore(store)
                .ListAsync(null, 0, 10, CancellationToken.None);
        var writeWithReadsOnly = () =>
            PersistedOperationsService
                .ForReads(factory)
                .UploadAsync(
                    new UploadPersistedOperationInput("svc_x", GraphQLFixture.ValidDocument),
                    CancellationToken.None
                );

        await readWithAStoreOnly.Should().ThrowAsync<InvalidOperationException>();
        await writeWithReadsOnly.Should().ThrowAsync<InvalidOperationException>();
        FluentActions
            .Invoking(() => PersistedOperationsService.ForStore(null!))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => PersistedOperationsService.ForReads(null!))
            .Should()
            .Throw<ArgumentNullException>();
    }

    [Test]
    public void AddPersistedOperationStore_RegistersTheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IDataContextProviderFactory>());
        Trax.Api.GraphQL.PersistedOperations.Extensions.ServiceCollectionPersistedOperationsExtensions.AddPersistedOperationStore(
            services,
            store => store.SingleNode()
        );

        using var sp = services.BuildServiceProvider();
        sp.GetService<IPersistedOperationsService>().Should().NotBeNull();
    }
}
