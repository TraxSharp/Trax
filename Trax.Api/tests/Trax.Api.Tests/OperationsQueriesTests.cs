using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;

namespace Trax.Api.Tests;

/// <summary>
/// Direct tests for OperationsQueries and DeadLetterQueries against an InMemory
/// data context. Bypasses the GraphQL executor since these are plain async methods
/// returning DTOs — exercising them this way gives full line coverage of the
/// pagination / cursor / count-estimator branches.
/// </summary>
[TestFixture]
public class OperationsQueriesTests
{
    // Use a per-class Postgres database so CountEstimator's pg_class query works.
    // Each test isolates by cleaning the affected tables in SetUp.
    // Pin pool size and prune idle connections aggressively so a long fixture
    // run can't exhaust Postgres' max_connections in CI.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=trax_api_operations;Username=trax;Password=trax123;"
        + "Maximum Pool Size=4;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t => t.AddEffects(e => e.UsePostgres(ConnectionString)));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public async Task SetUp()
    {
        // Clean the tables this fixture touches so each test starts fresh.
        await using var db = await _factory.CreateDbContextAsync(default);
        var ctx = (Microsoft.EntityFrameworkCore.DbContext)db;
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE trax.dead_letter, trax.metadata, trax.manifest, trax.manifest_group RESTART IDENTITY CASCADE"
        );
    }

    private async Task SeedManifests(int count, long? groupId = null)
    {
        groupId ??= await SeedManifestGroup("default-group");
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var m = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
            m.IsEnabled = i % 2 == 0;
            m.ManifestGroupId = groupId.Value;
            await db.Track(m);
        }
        await db.SaveChanges(default);
    }

    private async Task<long> SeedManifestGroup(string name = "test-group")
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var grp = new ManifestGroup
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await db.Track(grp);
        await db.SaveChanges(default);
        return grp.Id;
    }

    private async Task SeedExecutions(int count)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = $"Trax.X.Train{i}",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            await db.Track(meta);
        }
        await db.SaveChanges(default);
    }

    private async Task<Manifest> SeedManifestForDeadLetter()
    {
        var groupId = await SeedManifestGroup();
        await using var db = await _factory.CreateDbContextAsync(default);
        var m = Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });
        m.ManifestGroupId = groupId;
        await db.Track(m);
        await db.SaveChanges(default);
        return m;
    }

    private async Task SeedDeadLetters(int count, Manifest manifest)
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var dl = DeadLetter.Create(
                new CreateDeadLetter
                {
                    Manifest = manifest,
                    Reason = $"failure-{i}",
                    RetryCount = 3,
                }
            );
            await db.Track(dl);
        }
        await db.SaveChanges(default);
    }

    [Test]
    public async Task GetManifests_NoData_ReturnsEmptyResult()
    {
        var queries = new OperationsQueries();

        var result = await queries.GetManifests(_factory, default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.NextCursor.Should().BeNull();
    }

    [Test]
    public async Task GetManifests_PaginatesAndExposesCursor()
    {
        await SeedManifests(5);
        var queries = new OperationsQueries();

        var result = await queries.GetManifests(_factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetManifests_AfterIdCursor_FiltersAndUsesExactCount()
    {
        await SeedManifests(5);
        var queries = new OperationsQueries();
        var first = await queries.GetManifests(_factory, default, take: 2);

        var page2 = await queries.GetManifests(
            _factory,
            default,
            take: 2,
            afterId: first.NextCursor
        );

        page2.Items.Should().HaveCount(2);
        page2
            .Items.Select(m => m.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
        page2.IsEstimatedCount.Should().BeFalse();
    }

    [Test]
    public async Task GetManifests_SkipPagination_HonorsSkip()
    {
        await SeedManifests(5);
        var queries = new OperationsQueries();

        var result = await queries.GetManifests(_factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
        result.Skip.Should().Be(2);
    }

    [Test]
    public async Task GetManifest_ById_ReturnsRow()
    {
        await SeedManifests(2);
        var queries = new OperationsQueries();
        var first = (await queries.GetManifests(_factory, default)).Items.First();

        var fetched = await queries.GetManifest(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
    }

    [Test]
    public async Task GetManifest_MissingId_ReturnsNull()
    {
        var queries = new OperationsQueries();

        var fetched = await queries.GetManifest(99999, _factory, default);

        fetched.Should().BeNull();
    }

    [Test]
    public async Task GetGroups_PaginatesAndExposesCursor()
    {
        await SeedManifestGroup("g1");
        await SeedManifestGroup("g2");
        await SeedManifestGroup("g3");
        var queries = new ManifestGroupQueries();

        var result = await queries.GetGroups(_factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetGroups_AfterIdCursor_FiltersAndUsesExactCount()
    {
        await SeedManifestGroup("g1");
        await SeedManifestGroup("g2");
        await SeedManifestGroup("g3");
        var queries = new ManifestGroupQueries();
        var first = await queries.GetGroups(_factory, default, take: 1);

        var page2 = await queries.GetGroups(_factory, default, take: 2, afterId: first.NextCursor);

        page2.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task GetGroup_ReturnsSingleRecord_WhenIdMatches()
    {
        var seededId = await SeedManifestGroup("only");
        var queries = new ManifestGroupQueries();

        var fetched = await queries.GetGroup(seededId, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("only");
    }

    [Test]
    public async Task GetGroup_ReturnsNull_WhenIdDoesNotExist()
    {
        var queries = new ManifestGroupQueries();

        var fetched = await queries.GetGroup(999_999, _factory, default);

        fetched.Should().BeNull();
    }

    [Test]
    public async Task GetExecutions_PaginatesAndExposesCursor()
    {
        await SeedExecutions(3);
        var queries = new OperationsQueries();

        var result = await queries.GetExecutions(_factory, default, take: 2);

        result.Items.Should().HaveCount(2);
        result.NextCursor.Should().NotBeNull();
    }

    [Test]
    public async Task GetExecutions_AfterIdCursor_FiltersByCursor()
    {
        await SeedExecutions(4);
        var queries = new OperationsQueries();
        var first = await queries.GetExecutions(_factory, default, take: 1);

        var page2 = await queries.GetExecutions(
            _factory,
            default,
            take: 5,
            afterId: first.NextCursor
        );

        page2
            .Items.Select(e => e.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
    }

    [Test]
    public async Task GetExecutions_SkipPagination_Honors()
    {
        await SeedExecutions(5);
        var queries = new OperationsQueries();

        var result = await queries.GetExecutions(_factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task GetExecution_ById_ReturnsRow()
    {
        await SeedExecutions(1);
        var queries = new OperationsQueries();
        var first = (await queries.GetExecutions(_factory, default)).Items.First();

        var fetched = await queries.GetExecution(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
    }

    [Test]
    public async Task GetExecution_MissingId_ReturnsNull()
    {
        var queries = new OperationsQueries();

        (await queries.GetExecution(99999, _factory, default)).Should().BeNull();
    }

    [Test]
    public void GetTrains_NoTrainsRegistered_ReturnsEmpty()
    {
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        NSubstitute.SubstituteExtensions.Returns(
            discovery.DiscoverTrains(),
            new List<Trax.Mediator.Services.TrainDiscovery.TrainRegistration>()
        );
        var queries = new OperationsQueries();

        var result = queries.GetTrains(discovery);

        result.Should().BeEmpty();
    }

    [Test]
    public void GetTrains_HideAdminTrains_FiltersOutFrameworkTrains()
    {
        // The IManifestManagerTrain is a framework admin train listed in
        // AdminTrains.FullNames. With hideAdminTrains=true it should not appear.
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        var registrations = new List<Trax.Mediator.Services.TrainDiscovery.TrainRegistration>
        {
            FakeRegistration(typeof(Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain)),
            FakeRegistration(typeof(IUserTrain)),
        };
        NSubstitute.SubstituteExtensions.Returns(discovery.DiscoverTrains(), registrations);
        var queries = new OperationsQueries();

        var unfiltered = queries.GetTrains(discovery);
        var filtered = queries.GetTrains(discovery, hideAdminTrains: true);

        unfiltered.Should().HaveCount(2);
        filtered.Should().HaveCount(1);
        filtered.Single().ServiceTypeName.Should().NotContain("ManifestManager");
    }

    [Test]
    public void GetTrains_HideAdminTrainsFalse_ReturnsAll()
    {
        var discovery =
            NSubstitute.Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>();
        var registrations = new List<Trax.Mediator.Services.TrainDiscovery.TrainRegistration>
        {
            FakeRegistration(typeof(Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain)),
            FakeRegistration(typeof(IUserTrain)),
        };
        NSubstitute.SubstituteExtensions.Returns(discovery.DiscoverTrains(), registrations);
        var queries = new OperationsQueries();

        queries.GetTrains(discovery, hideAdminTrains: false).Should().HaveCount(2);
    }

    private static Trax.Mediator.Services.TrainDiscovery.TrainRegistration FakeRegistration(
        Type serviceType
    )
    {
        return new Trax.Mediator.Services.TrainDiscovery.TrainRegistration
        {
            ServiceType = serviceType,
            ImplementationType = serviceType,
            InputType = typeof(FakeInput),
            OutputType = typeof(FakeOutput),
            Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
            ServiceTypeName = serviceType.Name,
            ImplementationTypeName = serviceType.Name,
            InputTypeName = typeof(FakeInput).FullName!,
            OutputTypeName = typeof(FakeOutput).FullName!,
            RequiredPolicies = Array.Empty<string>(),
            RequiredRoles = Array.Empty<string>(),
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations =
                Trax.Effect.Attributes.GraphQLOperation.Run
                | Trax.Effect.Attributes.GraphQLOperation.Queue,
        };
    }

    private interface IUserTrain
        : Trax.Effect.Services.ServiceTrain.IServiceTrain<FakeInput, FakeOutput> { }

    [Test]
    public void DeadLettersNamespace_ReturnsNewInstance()
    {
        var queries = new OperationsQueries();

        var ns = queries.DeadLetters();

        ns.Should().NotBeNull();
    }

    [Test]
    public async Task GetDeadLetters_NoData_ReturnsEmpty()
    {
        var queries = new DeadLetterQueries();

        var result = await queries.GetDeadLetters(_factory, default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Test]
    public async Task GetDeadLetters_FiltersByStatus()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(3, manifest);
        var queries = new DeadLetterQueries();

        var result = await queries.GetDeadLetters(
            _factory,
            default,
            status: DeadLetterStatus.AwaitingIntervention
        );

        result.Items.Should().HaveCount(3);
    }

    [Test]
    public async Task GetDeadLetters_PaginatesWithCursor()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(4, manifest);
        var queries = new DeadLetterQueries();
        var first = await queries.GetDeadLetters(_factory, default, take: 2);

        var page2 = await queries.GetDeadLetters(
            _factory,
            default,
            take: 2,
            afterId: first.NextCursor
        );

        page2.Items.Should().HaveCount(2);
        page2
            .Items.Select(dl => dl.Id)
            .Should()
            .AllSatisfy(id => id.Should().BeLessThan(first.NextCursor!.Value));
    }

    [Test]
    public async Task GetDeadLetters_SkipHonored()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(5, manifest);
        var queries = new DeadLetterQueries();

        var result = await queries.GetDeadLetters(_factory, default, skip: 2, take: 2);

        result.Items.Should().HaveCount(2);
    }

    [Test]
    public async Task GetDeadLetter_ById_ReturnsRow()
    {
        var manifest = await SeedManifestForDeadLetter();
        await SeedDeadLetters(1, manifest);
        var queries = new DeadLetterQueries();
        var first = (await queries.GetDeadLetters(_factory, default)).Items.First();

        var fetched = await queries.GetDeadLetter(first.Id, _factory, default);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(first.Id);
    }

    [Test]
    public async Task GetDeadLetter_MissingId_ReturnsNull()
    {
        var queries = new DeadLetterQueries();

        (await queries.GetDeadLetter(99999, _factory, default)).Should().BeNull();
    }

    private interface ISomeFakeTrain
        : Trax.Effect.Services.ServiceTrain.IServiceTrain<FakeInput, FakeOutput> { }

    private class SomeFakeTrain { }

    public record FakeInput;

    public record FakeOutput;
}
