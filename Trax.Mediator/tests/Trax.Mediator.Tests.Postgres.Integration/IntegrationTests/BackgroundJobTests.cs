using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the BackgroundJob entity model and its persistence
/// to the <c>trax.background_job</c> PostgreSQL table.
/// </summary>
/// <remarks>
/// BackgroundJob is the queue table for the built-in PostgreSQL task server.
/// These tests verify:
/// - Factory method creates entities with correct defaults
/// - All fields persist and round-trip through PostgreSQL
/// - JSONB input column preserves structured data
/// - FetchedAt claim/release lifecycle
/// - Delete behavior (jobs are deleted on completion)
/// </remarks>
[TestFixture]
public class BackgroundJobTests : TestSetup
{
    #region Create Tests

    [Test]
    public async Task Create_WithRequiredFields_SetsDefaults()
    {
        // Arrange & Act
        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 42 },
            TimeProvider.System
        );

        // Assert
        job.MetadataId.Should().Be(42);
        job.Input.Should().BeNull();
        job.InputType.Should().BeNull();
        job.CreatedAt.Should()
            .BeCloseTo(TimeProvider.System.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(5));
        job.FetchedAt.Should().BeNull();
    }

    [Test]
    public async Task Create_WithAllFields_SetsAllProperties()
    {
        // Arrange & Act
        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = 99,
                Input = """{"region":"us-east"}""",
                InputType = "MyApp.SyncInput",
            },
            TimeProvider.System
        );

        // Assert
        job.MetadataId.Should().Be(99);
        job.Input.Should().Be("""{"region":"us-east"}""");
        job.InputType.Should().Be("MyApp.SyncInput");
        job.CreatedAt.Should()
            .BeCloseTo(TimeProvider.System.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(5));
        job.FetchedAt.Should().BeNull();
    }

    #endregion

    #region Persistence Tests

    [Test]
    public async Task Create_PersistsToDatabase()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 1 },
            TimeProvider.System
        );

        // Act
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);

        // Assert
        found.Should().NotBeNull();
        found.NotNull().MetadataId.Should().Be(1);
        found.Input.Should().BeNull();
        found.InputType.Should().BeNull();
        found
            .CreatedAt.Should()
            .BeCloseTo(TimeProvider.System.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(10));
        found.FetchedAt.Should().BeNull();
    }

    [Test]
    public async Task Create_WithInput_PersistsToDatabase()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = 2,
                Input = """{"name":"test","count":5}""",
                InputType = "Trax.Core.Tests.TestInput",
            },
            TimeProvider.System
        );

        // Act
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);

        // Assert
        found.Should().NotBeNull();
        found.NotNull().MetadataId.Should().Be(2);
        found.Input.Should().NotBeNull();
        found.InputType.Should().Be("Trax.Core.Tests.TestInput");
    }

    [Test]
    public async Task Create_GeneratesAutoIncrementId()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job1 = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 10 },
            TimeProvider.System
        );
        var job2 = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 20 },
            TimeProvider.System
        );

        // Act
        await context.Track(job1);
        await context.SaveChanges(CancellationToken.None);

        await context.Track(job2);
        await context.SaveChanges(CancellationToken.None);

        // Assert
        job1.Id.Should().BeGreaterThan(0);
        job2.Id.Should().BeGreaterThan(job1.Id);
    }

    #endregion

    #region JSONB Round-Trip Tests

    [Test]
    public async Task InputJsonb_SimpleObject_RoundTrips()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = 3,
                Input = """{"region":"us-east","batchSize":500}""",
                InputType = "MyApp.SyncInput",
            },
            TimeProvider.System
        );

        // Act
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);

        // Assert
        found.Should().NotBeNull();
        found.NotNull().Input.Should().NotBeNull();
        found.Input.Should().Contain("\"region\"");
        found.Input.Should().Contain("\"us-east\"");
        found.Input.Should().Contain("\"batchSize\"");
        found.Input.Should().Contain("500");
    }

    [Test]
    public async Task InputJsonb_ComplexNestedObject_RoundTrips()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var complexJson =
            """{"name":"test","nested":{"key":"value","numbers":[1,2,3]},"enabled":true}""";

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = 4,
                Input = complexJson,
                InputType = "Trax.Core.Tests.ComplexInput",
            },
            TimeProvider.System
        );

        // Act
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);

        // Assert
        found.Should().NotBeNull();
        found.NotNull().Input.Should().NotBeNull();
        // JSONB normalizes whitespace but preserves data
        found.Input.Should().Contain("\"name\"");
        found.Input.Should().Contain("\"test\"");
        found.Input.Should().Contain("\"nested\"");
        found.Input.Should().Contain("[1, 2, 3]");
        found.Input.Should().Contain("true");
    }

    [Test]
    public async Task InputJsonb_NullInput_PersistsAsNull()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 5 },
            TimeProvider.System
        );

        // Act
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);

        // Assert
        found.Should().NotBeNull();
        found.NotNull().Input.Should().BeNull();
        found.InputType.Should().BeNull();
    }

    #endregion

    #region FetchedAt Lifecycle Tests

    [Test]
    public async Task FetchedAt_InitiallyNull_IndicatesAvailable()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 6 },
            TimeProvider.System
        );

        // Act
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);

        // Assert - FetchedAt should be null (available for dequeue)
        found.Should().NotBeNull();
        found.NotNull().FetchedAt.Should().BeNull();
    }

    [Test]
    public async Task FetchedAt_SetOnClaim_PersistsTimestamp()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 7 },
            TimeProvider.System
        );
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);

        // Act - Simulate worker claiming the job
        var claimTime = TimeProvider.System.GetUtcNow().UtcDateTime;
        job.FetchedAt = claimTime;
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);

        // Assert
        found.Should().NotBeNull();
        found.NotNull().FetchedAt.Should().NotBeNull();
        found.FetchedAt.Should().BeCloseTo(claimTime, TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task FetchedAt_StaleTimestamp_IdentifiesAbandonedJob()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 8 },
            TimeProvider.System
        );
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);

        // Simulate a worker that claimed the job 40 minutes ago (stale beyond 30m visibility timeout)
        job.FetchedAt = TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-40);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act - Query for jobs eligible for re-claim (fetched_at older than visibility timeout)
        var visibilityTimeout = TimeSpan.FromMinutes(30);
        var cutoff = TimeProvider.System.GetUtcNow().UtcDateTime - visibilityTimeout;

        var staleJobs = await context
            .BackgroundJobs.Where(j => j.FetchedAt != null && j.FetchedAt < cutoff)
            .ToListAsync();

        // Assert - The abandoned job should be found
        staleJobs.Should().Contain(j => j.Id == job.Id);
    }

    #endregion

    #region Delete Tests

    [Test]
    public async Task Delete_RemovesJobFromDatabase()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 9 },
            TimeProvider.System
        );
        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        var jobId = job.Id;
        context.Reset();

        // Act - Simulate worker deleting the job after execution
        var toDelete = await context.BackgroundJobs.FindAsync(jobId);
        toDelete.Should().NotBeNull();
        context.BackgroundJobs.Remove(toDelete.NotNull());
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Assert
        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == jobId);
        found.Should().BeNull();
    }

    #endregion

    #region Query Tests

    [Test]
    public async Task Query_AvailableJobs_ExcludesClaimedJobs()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var availableJob = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 10 },
            TimeProvider.System
        );
        var claimedJob = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 11 },
            TimeProvider.System
        );

        await context.Track(availableJob);
        await context.Track(claimedJob);
        await context.SaveChanges(CancellationToken.None);

        // Claim one job
        claimedJob.FetchedAt = TimeProvider.System.GetUtcNow().UtcDateTime;
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act - Query for available (unclaimed) jobs
        var available = await context.BackgroundJobs.Where(j => j.FetchedAt == null).ToListAsync();

        // Assert
        available.Should().Contain(j => j.Id == availableJob.Id);
        available.Should().NotContain(j => j.Id == claimedJob.Id);
    }

    [Test]
    public async Task Query_OrderByCreatedAt_ReturnsOldestFirst()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job1 = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 12 },
            TimeProvider.System
        );
        await context.Track(job1);
        await context.SaveChanges(CancellationToken.None);

        await Task.Delay(50); // Ensure different timestamps

        var job2 = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 13 },
            TimeProvider.System
        );
        await context.Track(job2);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var ordered = await context
            .BackgroundJobs.Where(j => j.FetchedAt == null)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();

        // Assert - First job should come before second
        var idx1 = ordered.FindIndex(j => j.Id == job1.Id);
        var idx2 = ordered.FindIndex(j => j.Id == job2.Id);
        idx1.Should().BeLessThan(idx2, "older job should be dequeued first");
    }

    [Test]
    public async Task Query_MultipleJobs_CanFilterByMetadataId()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);

        var job1 = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 100 },
            TimeProvider.System
        );
        var job2 = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 200 },
            TimeProvider.System
        );
        var job3 = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = 100 },
            TimeProvider.System
        );

        await context.Track(job1);
        await context.Track(job2);
        await context.Track(job3);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var forMetadata100 = await context
            .BackgroundJobs.Where(j => j.MetadataId == 100)
            .ToListAsync();

        // Assert
        forMetadata100.Should().HaveCount(2);
        forMetadata100.Should().AllSatisfy(j => j.MetadataId.Should().Be(100));
    }

    #endregion

    #region ToString Tests

    [Test]
    public void ToString_ReturnsJsonRepresentation()
    {
        // Arrange
        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = 42,
                Input = """{"key":"value"}""",
                InputType = "MyApp.TestInput",
            },
            TimeProvider.System
        );

        // Act
        var result = job.ToString();

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("42");
        result.Should().Contain("MyApp.TestInput");
    }

    #endregion
}
