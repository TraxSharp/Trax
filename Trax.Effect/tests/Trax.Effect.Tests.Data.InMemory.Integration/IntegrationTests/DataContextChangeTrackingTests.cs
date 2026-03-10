using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.InMemory.Services.InMemoryContext;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata.DTOs;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// Tests that validate the DataContext.Track() and DataContext.Update() change-tracking
/// behavior, particularly the fix that prevents base.Update() from forcing an already-tracked
/// entity's state from Added to Modified (which broke the InMemory provider).
/// </summary>
public class DataContextChangeTrackingTests
{
    private InMemoryContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<InMemoryContext>().UseInMemoryDatabase(dbName).Options);

    #region Track Tests

    [Test]
    public async Task Track_NewDetachedEntity_SetsStateToAdded()
    {
        var context = CreateContext(nameof(Track_NewDetachedEntity_SetsStateToAdded));

        var metadata = CreateMetadata("TrackNew");

        await context.Track(metadata);

        context.Entry(metadata).State.Should().Be(EntityState.Added);
    }

    [Test]
    public async Task Track_AlreadyAddedEntity_DoesNotChangeState()
    {
        // This is the core bug fix: Track must not call base.Update() on an
        // entity already in Added state, because base.Update() would force it
        // to Modified, and InMemory can't UPDATE a row that doesn't exist yet.
        var context = CreateContext(nameof(Track_AlreadyAddedEntity_DoesNotChangeState));

        var metadata = CreateMetadata("AlreadyAdded");
        await context.Track(metadata);
        context.Entry(metadata).State.Should().Be(EntityState.Added);

        // Second Track call should not change state
        await context.Track(metadata);
        context.Entry(metadata).State.Should().Be(EntityState.Added);
    }

    [Test]
    public async Task Track_AlreadyUnchangedEntity_DoesNotChangeState()
    {
        // After Track → Save, the entity becomes Unchanged. A second Track
        // call (e.g. from a scheduler job runner flow) should leave it alone.
        var context = CreateContext(nameof(Track_AlreadyUnchangedEntity_DoesNotChangeState));

        var metadata = CreateMetadata("AlreadyUnchanged");
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Entry(metadata).State.Should().Be(EntityState.Unchanged);

        await context.Track(metadata);
        context.Entry(metadata).State.Should().Be(EntityState.Unchanged);
    }

    [Test]
    public async Task Track_AlreadyModifiedEntity_DoesNotChangeState()
    {
        var context = CreateContext(nameof(Track_AlreadyModifiedEntity_DoesNotChangeState));

        var metadata = CreateMetadata("AlreadyModified");
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        // Mutate to make it Modified
        metadata.TrainState = TrainState.Completed;
        context.Entry(metadata).State.Should().Be(EntityState.Modified);

        // Track should not interfere
        await context.Track(metadata);
        context.Entry(metadata).State.Should().Be(EntityState.Modified);
    }

    [Test]
    public async Task Track_NewEntity_CanBeSavedAndQueried()
    {
        var context = CreateContext(nameof(Track_NewEntity_CanBeSavedAndQueried));

        var metadata = CreateMetadata("SaveAndQuery");
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        metadata.Id.Should().BeGreaterThan(0);

        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("SaveAndQuery");
    }

    #endregion

    #region Update Tests

    [Test]
    public async Task Update_DetachedEntity_AttachesAsModified()
    {
        // Simulates the scheduler job runner flow: metadata was loaded by a
        // different context, so it appears Detached to this context.
        var dbName = nameof(Update_DetachedEntity_AttachesAsModified);
        var contextA = CreateContext(dbName);

        // Create and save in context A
        var metadata = CreateMetadata("DetachedUpdate");
        await contextA.Track(metadata);
        await contextA.SaveChanges(CancellationToken.None);

        var savedId = metadata.Id;
        savedId.Should().BeGreaterThan(0);

        // Load in context B (simulate different EF context)
        var contextB = CreateContext(dbName);
        var loaded = await contextB.Metadatas.FirstAsync(x => x.Id == savedId);

        // Detach from context B
        contextB.Reset();

        // Now pass to a third context (like the inner train's EffectRunner context)
        var contextC = CreateContext(dbName);
        contextC.Entry(loaded).State.Should().Be(EntityState.Detached);

        // Update should attach as Modified (Id > 0)
        await contextC.Update(loaded);
        contextC.Entry(loaded).State.Should().Be(EntityState.Modified);
    }

    [Test]
    public async Task Update_AlreadyTrackedEntity_IsNoOp()
    {
        var context = CreateContext(nameof(Update_AlreadyTrackedEntity_IsNoOp));

        var metadata = CreateMetadata("TrackedUpdate");
        await context.Track(metadata);
        context.Entry(metadata).State.Should().Be(EntityState.Added);

        // Update should NOT change state from Added to Modified
        await context.Update(metadata);
        context.Entry(metadata).State.Should().Be(EntityState.Added);
    }

    [Test]
    public async Task Update_AfterSave_MutationsDetectedByChangeTracking()
    {
        var context = CreateContext(nameof(Update_AfterSave_MutationsDetectedByChangeTracking));

        var metadata = CreateMetadata("ChangeTracking");
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        // Mutate the entity (it's Unchanged → becomes Modified via snapshot)
        metadata.TrainState = TrainState.Failed;
        metadata.EndTime = DateTime.UtcNow;

        // Update is a no-op for already-tracked entities
        await context.Update(metadata);

        // But SaveChanges should still detect the mutations via snapshot comparison
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var found = await context.Metadatas.FirstAsync(x => x.Id == metadata.Id);
        found.TrainState.Should().Be(TrainState.Failed);
        found.EndTime.Should().NotBeNull();
    }

    #endregion

    #region Full ServiceTrain-like Flow

    [Test]
    public async Task FullFlow_Track_Update_Save_Mutate_Update_Save()
    {
        // Simulates the exact DataContext call sequence during a ServiceTrain run:
        // 1. InitializeServiceTrain → Track(metadata)
        // 2. StartServiceTrain → Update(metadata) [state = InProgress]
        // 3. ServiceTrain.Run → SaveChanges() [first persist]
        // 4. RunInternal completes
        // 5. FinishServiceTrain → Update(metadata) [state = Completed]
        // 6. ServiceTrain.Run → SaveChanges() [second persist]
        var context = CreateContext(nameof(FullFlow_Track_Update_Save_Mutate_Update_Save));

        var metadata = CreateMetadata("FullFlow");

        // Step 1: Track
        await context.Track(metadata);
        context.Entry(metadata).State.Should().Be(EntityState.Added);

        // Step 2: Update with state change (before first save)
        metadata.TrainState = TrainState.InProgress;
        await context.Update(metadata);
        context
            .Entry(metadata)
            .State.Should()
            .Be(EntityState.Added, "Must stay Added, not Modified");

        // Step 3: First SaveChanges (INSERT)
        await context.SaveChanges(CancellationToken.None);
        metadata.Id.Should().BeGreaterThan(0);
        context.Entry(metadata).State.Should().Be(EntityState.Unchanged);

        // Step 4-5: Finish with final state
        metadata.TrainState = TrainState.Completed;
        metadata.EndTime = DateTime.UtcNow;
        await context.Update(metadata);

        // Step 6: Second SaveChanges (UPDATE)
        await context.SaveChanges(CancellationToken.None);

        // Verify final state
        context.Reset();
        var found = await context.Metadatas.FirstAsync(x => x.Id == metadata.Id);
        found.TrainState.Should().Be(TrainState.Completed);
        found.EndTime.Should().NotBeNull();
    }

    [Test]
    public async Task FullFlow_FailedTrain_PersistsFailureState()
    {
        // Same as FullFlow but for the error path
        var context = CreateContext(nameof(FullFlow_FailedTrain_PersistsFailureState));

        var metadata = CreateMetadata("FailedFlow");

        await context.Track(metadata);
        metadata.TrainState = TrainState.InProgress;
        await context.Update(metadata);
        await context.SaveChanges(CancellationToken.None);

        // Error path
        metadata.TrainState = TrainState.Failed;
        metadata.EndTime = DateTime.UtcNow;
        await context.Update(metadata);
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var found = await context.Metadatas.FirstAsync(x => x.Id == metadata.Id);
        found.TrainState.Should().Be(TrainState.Failed);
    }

    [Test]
    public async Task CrossContext_EntitySavedInOneContext_CanBeTrackedByAnother()
    {
        // Simulates the scheduler flow where metadata is loaded by LoadMetadataStep's
        // context, then passed to the inner train's EffectRunner context.
        var dbName = nameof(CrossContext_EntitySavedInOneContext_CanBeTrackedByAnother);

        // Context A: create and save metadata (like the scheduler's DataContext)
        var contextA = CreateContext(dbName);
        var metadata = CreateMetadata("CrossContext");
        await contextA.Track(metadata);
        await contextA.SaveChanges(CancellationToken.None);
        var savedId = metadata.Id;

        // Load from DB in context A (like LoadMetadataStep)
        contextA.Reset();
        var loaded = await contextA.Metadatas.FirstAsync(x => x.Id == savedId);

        // Context B: inner train's EffectRunner context
        var contextB = CreateContext(dbName);

        // Track should attach the detached entity (not try to INSERT)
        await contextB.Track(loaded);
        contextB
            .Entry(loaded)
            .State.Should()
            .Be(EntityState.Modified, "Detached entity with Id > 0 should be Modified via Update");

        // Modify and save
        loaded.TrainState = TrainState.InProgress;
        await contextB.Update(loaded);
        await contextB.SaveChanges(CancellationToken.None);

        // Verify
        contextB.Reset();
        var found = await contextB.Metadatas.FirstAsync(x => x.Id == savedId);
        found.TrainState.Should().Be(TrainState.InProgress);
    }

    #endregion

    #region Helpers

    private static Metadata CreateMetadata(string name) =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

    #endregion
}
