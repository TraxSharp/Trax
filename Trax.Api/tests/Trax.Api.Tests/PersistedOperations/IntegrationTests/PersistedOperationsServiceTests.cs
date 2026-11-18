using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.PersistedOperations.GraphQL;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Core.Extensions;
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
    private ServiceProvider? _sp;
    private IPersistedOperationsService? _service;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping integration tests.");

        _sp = await GraphQLFixture.BuildAsync();
        _service = _sp.NotNull().GetRequiredService<IPersistedOperationsService>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_sp is not null)
            if (_sp is not null)
                await _sp.DisposeAsync();
    }

    [SetUp]
    public Task SetUp() => PostgresFixture.ClearAsync();

    [Test]
    public async Task Upload_Deactivate_Restore_RoundTrip_ThroughTheService()
    {
        var uploaded = await _service
            .NotNull()
            .UploadAsync(
                new UploadPersistedOperationInput("svc_v1", GraphQLFixture.ValidDocument),
                CancellationToken.None
            );
        uploaded.Success.Should().BeTrue();

        var deactivated = await _service
            .NotNull()
            .DeactivateAsync(
                new DeactivatePersistedOperationInput("svc_v1", "retired"),
                CancellationToken.None
            );
        deactivated.Success.Should().BeTrue();
        (await _service.NotNull().GetAsync("svc_v1", tenantKey: null, CancellationToken.None))
            .NotNull()
            .IsActive.Should()
            .BeFalse("a deactivated operation is still readable");

        var restored = await _service
            .NotNull()
            .RestoreAsync(new RestorePersistedOperationInput("svc_v1"), CancellationToken.None);
        restored.Success.Should().BeTrue();

        var history = await _service
            .NotNull()
            .GetHistoryAsync("svc_v1", tenantKey: null, 0, 50, CancellationToken.None);
        history.Select(h => h.ChangeType).Should().Equal("Restore", "Deactivate", "Upsert");

        var page = await _service.NotNull().ListAsync(filter: null, 0, 50, CancellationToken.None);
        page.TotalCount.Should().Be(1);
    }

    [Test]
    public async Task TheServiceAndTheKeptResolverOverloads_RefuseTheSameUpload()
    {
        var input = new UploadPersistedOperationInput(
            "two_ops",
            "query A { hello } query B { version }"
        );

        var viaService = await _service.NotNull().UploadAsync(input, CancellationToken.None);
        var viaResolver = await new PersistedOperationMutations().UploadPersistedOperation(
            input,
            _sp.NotNull().GetRequiredService<IPersistedOperationStore>(),
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
        var payload = await _service
            .NotNull()
            .DeactivateAsync(
                new DeactivatePersistedOperationInput("missing", "gone"),
                CancellationToken.None
            );

        payload.Success.Should().BeFalse();
        payload.Errors[0].Code.Should().Be("NOT_FOUND");
    }

    [Test]
    public async Task TheKeptQueryOverloads_ReadWhatTheServiceReads()
    {
        await _service
            .NotNull()
            .UploadAsync(
                new UploadPersistedOperationInput("svc_read", GraphQLFixture.ValidDocument),
                CancellationToken.None
            );
        var factory = _sp.NotNull().GetRequiredService<IDataContextProviderFactory>();

        var viaResolver = await new PersistedOperationQueries().PersistedOperation(
            "svc_read",
            factory,
            CancellationToken.None
        );
        var viaService = await _service
            .NotNull()
            .GetAsync("svc_read", tenantKey: null, CancellationToken.None);

        viaResolver.Should().BeEquivalentTo(viaService);
    }

    [Test]
    public async Task DeactivatingAnInactiveOperation_Succeeds_AndSendsTheChangeAgain()
    {
        await _service
            .NotNull()
            .UploadAsync(
                new UploadPersistedOperationInput("svc_twice", GraphQLFixture.ValidDocument),
                CancellationToken.None
            );
        await _service
            .NotNull()
            .DeactivateAsync(
                new DeactivatePersistedOperationInput("svc_twice", "retired"),
                CancellationToken.None
            );
        var generation = _sp.NotNull().GetRequiredService<PersistedOperationCacheGeneration>();
        var before = generation.Current;

        var again = await _service
            .NotNull()
            .DeactivateAsync(
                new DeactivatePersistedOperationInput("svc_twice", "retired, sent again"),
                CancellationToken.None
            );

        again.Success.Should().BeTrue("deactivating is idempotent");
        again.Operation.NotNull().IsActive.Should().BeFalse();
        again.Operation.DeprecationReason.Should().Be("retired, sent again");
        generation.Current.Should().BeGreaterThan(before, "the caches are emptied again");
    }

    [Test]
    public async Task TheKeptListAndHistoryOverloads_ReadWhatTheServiceReads()
    {
        await _service
            .NotNull()
            .UploadAsync(
                new UploadPersistedOperationInput("svc_list", GraphQLFixture.ValidDocument),
                CancellationToken.None
            );
        var factory = _sp.NotNull().GetRequiredService<IDataContextProviderFactory>();
        var queries = new PersistedOperationQueries();

        (await queries.PersistedOperations(factory, CancellationToken.None))
            .Should()
            .BeEquivalentTo(
                await _service.NotNull().ListAsync(filter: null, 0, 50, CancellationToken.None)
            );
        (await queries.PersistedOperationHistory("svc_list", factory, CancellationToken.None))
            .Should()
            .BeEquivalentTo(
                await _service
                    .NotNull()
                    .GetHistoryAsync("svc_list", tenantKey: null, 0, 50, CancellationToken.None)
            );
    }

    [Test]
    public async Task TheKeptDeactivateAndRestoreOverloads_ChangeTheOperation()
    {
        var store = _sp.NotNull().GetRequiredService<IPersistedOperationStore>();
        var factory = _sp.NotNull().GetRequiredService<IDataContextProviderFactory>();
        await store.UpsertAsync(
            "svc_kept",
            GraphQLFixture.ValidDocument,
            options: null,
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
        (await store.GetAsync("svc_kept", tenantKey: null, CancellationToken.None))
            .Should()
            .NotBeNull();
    }

    [Test]
    public async Task List_FiltersByTenant_AndClampsThePage()
    {
        await _service
            .NotNull()
            .UploadAsync(
                new UploadPersistedOperationInput("svc_t_none", GraphQLFixture.ValidDocument),
                CancellationToken.None
            );
        await _service
            .NotNull()
            .UploadAsync(
                new UploadPersistedOperationInput(
                    "svc_t_acme",
                    GraphQLFixture.ValidDocument,
                    TenantKey: "acme"
                ),
                CancellationToken.None
            );

        var noTenant = await _service
            .NotNull()
            .ListAsync(new PersistedOperationFilter(TenantKey: ""), -5, 0, CancellationToken.None);
        var acme = await _service
            .NotNull()
            .ListAsync(
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
        var store = _sp.NotNull().GetRequiredService<IPersistedOperationStore>();
        var factory = _sp.NotNull().GetRequiredService<IDataContextProviderFactory>();

        var readWithAStoreOnly = () =>
            PersistedOperationsService
                .ForStore(store)
                .ListAsync(filter: null, 0, 10, CancellationToken.None);
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
            .Invoking(() =>
                PersistedOperationsService.ForStore(
                    Null.Of<Trax.Api.GraphQL.PersistedOperations.Storage.IPersistedOperationStore>()
                )
            )
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() =>
                PersistedOperationsService.ForReads(
                    Null.Of<Trax.Effect.Data.Services.IDataContextFactory.IDataContextProviderFactory>()
                )
            )
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
