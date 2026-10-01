using FluentAssertions;
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
    public void AddPersistedOperationStore_RegistersTheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IDataContextProviderFactory>());
        Trax.Api.GraphQL.PersistedOperations.Extensions.ServiceCollectionPersistedOperationsExtensions.AddPersistedOperationStore(
            services,
            "Host=fake;Database=fake"
        );

        using var sp = services.BuildServiceProvider();
        sp.GetService<IPersistedOperationsService>().Should().NotBeNull();
    }
}
