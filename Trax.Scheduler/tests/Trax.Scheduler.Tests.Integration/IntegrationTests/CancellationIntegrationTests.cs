using System.Reflection;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StepProvider.Progress.Services.CancellationCheckProvider;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Tests.Integration.Examples.Trains;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the cancellation feature set:
/// - <see cref="CancellationCheckProvider"/> — checks DB cancel flag before each step
/// - <see cref="CancellationRegistry"/> — same-server cancel via CTS
/// - <see cref="TrainState.Cancelled"/> — terminal state for cancelled trains
/// </summary>
[TestFixture]
public class CancellationIntegrationTests : TestSetup
{
    #region CancellationRequested DB Flag Tests

    [Test]
    public async Task CancellationRequested_DefaultsToFalse_OnNewMetadata()
    {
        // Arrange
        var metadata = await CreateTestMetadata();

        // Assert
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metadata.Id);
        loaded.Should().NotBeNull();
        loaded!.CancellationRequested.Should().BeFalse();
    }

    [Test]
    public async Task CancellationRequested_CanBeSetToTrue_ViaExecuteUpdate()
    {
        // Arrange
        var metadata = await CreateTestMetadata();

        // Act
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.CancellationRequested, true),
                CancellationToken.None
            );
        DataContext.Reset();

        // Assert
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metadata.Id);
        loaded!.CancellationRequested.Should().BeTrue();
    }

    #endregion

    #region StepProgress Column Tests

    [Test]
    public async Task StepProgress_DefaultsToNull_OnNewMetadata()
    {
        // Arrange
        var metadata = await CreateTestMetadata();

        // Assert
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metadata.Id);
        loaded!.CurrentlyRunningStep.Should().BeNull();
        loaded!.StepStartedAt.Should().BeNull();
    }

    [Test]
    public async Task StepProgress_CanBeSetAndCleared()
    {
        // Arrange
        var metadata = await CreateTestMetadata();
        var stepStartTime = DateTime.UtcNow;

        // Act — set step progress
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(m => m.CurrentlyRunningStep, "FetchDataStep")
                        .SetProperty(m => m.StepStartedAt, stepStartTime),
                CancellationToken.None
            );
        DataContext.Reset();

        // Assert — set
        var withProgress = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metadata.Id);
        withProgress!.CurrentlyRunningStep.Should().Be("FetchDataStep");
        withProgress!.StepStartedAt.Should().BeCloseTo(stepStartTime, TimeSpan.FromSeconds(1));

        // Act — clear step progress
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(m => m.CurrentlyRunningStep, (string?)null)
                        .SetProperty(m => m.StepStartedAt, (DateTime?)null),
                CancellationToken.None
            );
        DataContext.Reset();

        // Assert — cleared
        var cleared = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metadata.Id);
        cleared!.CurrentlyRunningStep.Should().BeNull();
        cleared!.StepStartedAt.Should().BeNull();
    }

    #endregion

    #region CancellationCheckProvider Integration Tests

    [Test]
    public async Task CancellationCheckProvider_NotCancelled_DoesNotThrow()
    {
        // Arrange
        var metadata = await CreateTestMetadata();
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = new CancellationCheckProvider(factory);

        var train = CreateTrainWithMetadata(metadata);
        var step = new TestProgressStep();

        // Act & Assert
        var act = () => provider.BeforeStepExecution(step, train, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task CancellationCheckProvider_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var metadata = await CreateTestMetadata();

        // Set cancel flag in DB
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.CancellationRequested, true),
                CancellationToken.None
            );
        DataContext.Reset();

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = new CancellationCheckProvider(factory);

        // Reload metadata to get the updated state
        var reloadedMetadata = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        var train = CreateTrainWithMetadata(reloadedMetadata);
        var step = new TestProgressStep();

        // Act & Assert
        var act = () => provider.BeforeStepExecution(step, train, CancellationToken.None);
        await act.Should()
            .ThrowAsync<OperationCanceledException>()
            .WithMessage("*cancellation requested*");
    }

    [Test]
    public async Task CancellationCheckProvider_NullMetadata_DoesNotThrow()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = new CancellationCheckProvider(factory);

        var train = new TestProgressTrain();
        // Leave Metadata as null (default)
        var step = new TestProgressStep();

        // Act & Assert
        var act = () => provider.BeforeStepExecution(step, train, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task CancellationCheckProvider_AfterStepExecution_IsNoOp()
    {
        // Arrange
        var metadata = await CreateTestMetadata();
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var provider = new CancellationCheckProvider(factory);

        var train = CreateTrainWithMetadata(metadata);
        var step = new TestProgressStep();

        // Act & Assert — should complete without doing anything
        var act = () => provider.AfterStepExecution(step, train, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region CancellationRegistry + Worker Integration Tests

    [Test]
    public async Task Registry_RegisterAndCancel_CancelsToken()
    {
        // Arrange
        var registry = new CancellationRegistry();
        using var cts = new CancellationTokenSource();
        var metadata = await CreateTestMetadata();

        // Act
        registry.Register(metadata.Id, cts);
        var cancelled = registry.TryCancel(metadata.Id);

        // Assert
        cancelled.Should().BeTrue();
        cts.IsCancellationRequested.Should().BeTrue();

        // Cleanup
        registry.Unregister(metadata.Id);
    }

    [Test]
    public async Task Registry_UnregisterAfterExecution_CancelReturnsFalse()
    {
        // Arrange
        var registry = new CancellationRegistry();
        using var cts = new CancellationTokenSource();
        var metadata = await CreateTestMetadata();

        // Simulate the PostgresWorkerService lifecycle
        registry.Register(metadata.Id, cts);
        // ... train executes ...
        registry.Unregister(metadata.Id);

        // Act — try to cancel after train completed
        var cancelled = registry.TryCancel(metadata.Id);

        // Assert
        cancelled.Should().BeFalse();
        cts.IsCancellationRequested.Should().BeFalse();
    }

    #endregion

    #region TrainState.Cancelled Persistence Tests

    [Test]
    public async Task CancelledState_PersistsToDatabase()
    {
        // Arrange
        var metadata = await CreateTestMetadata();

        // Act — set to Cancelled
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.TrainState, TrainState.Cancelled),
                CancellationToken.None
            );
        DataContext.Reset();

        // Assert
        var loaded = await DataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metadata.Id);
        loaded!.TrainState.Should().Be(TrainState.Cancelled);
    }

    [Test]
    public async Task CancelledState_IsQueryable()
    {
        // Arrange — create multiple metadata with different states
        var m1 = await CreateTestMetadata();
        var m2 = await CreateTestMetadata();
        var m3 = await CreateTestMetadata();

        await DataContext
            .Metadatas.Where(m => m.Id == m1.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.TrainState, TrainState.Cancelled),
                CancellationToken.None
            );
        await DataContext
            .Metadatas.Where(m => m.Id == m2.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.TrainState, TrainState.Completed),
                CancellationToken.None
            );
        await DataContext
            .Metadatas.Where(m => m.Id == m3.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.TrainState, TrainState.Cancelled),
                CancellationToken.None
            );
        DataContext.Reset();

        // Act
        var cancelledCount = await DataContext
            .Metadatas.AsNoTracking()
            .CountAsync(m => m.TrainState == TrainState.Cancelled);

        // Assert
        cancelledCount.Should().Be(2);
    }

    [Test]
    public async Task CancelledState_IsTerminal_IncludedInCleanupQueries()
    {
        // Arrange — Cancelled metadata should be queryable alongside Completed and Failed
        var metadata = await CreateTestMetadata();
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.TrainState, TrainState.Cancelled),
                CancellationToken.None
            );
        DataContext.Reset();

        // Act — query for terminal states (mirrors DeleteExpiredMetadataStep logic)
        var terminalCount = await DataContext
            .Metadatas.AsNoTracking()
            .CountAsync(m =>
                m.TrainState == TrainState.Completed
                || m.TrainState == TrainState.Failed
                || m.TrainState == TrainState.Cancelled
            );

        // Assert
        terminalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Helper Methods

    private async Task<Metadata> CreateTestMetadata()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "test" },
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    /// <summary>
    /// Creates a TestProgressTrain with Metadata set via reflection (internal setter).
    /// </summary>
    private static TestProgressTrain CreateTrainWithMetadata(Metadata metadata)
    {
        var train = new TestProgressTrain();
        var prop = typeof(ServiceTrain<string, string>).GetProperty(
            "Metadata",
            BindingFlags.Public | BindingFlags.Instance
        );
        prop?.SetValue(train, metadata);
        return train;
    }

    #endregion
}

/// <summary>
/// Minimal concrete EffectTrain for testing step effect providers.
/// </summary>
public class TestProgressTrain : ServiceTrain<string, string>
{
    protected override Task<Either<Exception, string>> RunInternal(string input) =>
        Task.FromResult<Either<Exception, string>>(input);
}

/// <summary>
/// Minimal concrete EffectStep for testing step effect providers.
/// </summary>
public class TestProgressStep : EffectStep<string, string>
{
    public override Task<string> Run(string input) => Task.FromResult(input);
}
