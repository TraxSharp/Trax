using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;
using Trax.Effect.Models;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="JunctionProgressProvider"/>, the junction effect provider
/// that writes currently-running junction name and timestamp to train metadata. It writes those two
/// columns through a context of its own and never saves through the run's effect runner, which
/// would commit everything else the run has tracked so far.
/// </summary>
[TestFixture]
public class JunctionProgressProviderTests
{
    private JunctionProgressProvider _provider;
    private FakeEffectRunner _fakeEffectRunner;
    private ServiceProvider _services;
    private RecordingFactory _factory;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        _services = services.BuildServiceProvider();
        _factory = new RecordingFactory(
            _services.GetRequiredService<IDataContextProviderFactory>()
        );
        _provider = new JunctionProgressProvider(_factory);
        _fakeEffectRunner = new FakeEffectRunner();
    }

    [TearDown]
    public async Task TearDown()
    {
        _provider.Dispose();
        _fakeEffectRunner.Dispose();
        await _services.DisposeAsync();
    }

    #region BeforeJunctionExecution Tests

    [Test]
    public async Task BeforeJunctionExecution_SetsCurrentlyRunningJunction()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");

        // Act
        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        // Assert
        train.Metadata!.CurrentlyRunningJunction.Should().Be("ProcessDataJunction");
    }

    [Test]
    public async Task BeforeJunctionExecution_SetsJunctionStartedAt()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");
        var before = DateTime.UtcNow;

        // Act
        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        // Assert
        train.Metadata!.JunctionStartedAt.Should().NotBeNull();
        train.Metadata!.JunctionStartedAt.Should().BeOnOrAfter(before);
        train.Metadata!.JunctionStartedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Test]
    public async Task BeforeJunctionExecution_WritesTheProgressColumnsToTheRow()
    {
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");

        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        var row = await StoredRow(train);
        row.CurrentlyRunningJunction.Should().Be("ProcessDataJunction");
        row.JunctionStartedAt.Should().NotBeNull();
    }

    [Test]
    public async Task BeforeJunctionExecution_DoesNotSaveThroughTheEffectRunner()
    {
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");

        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        _fakeEffectRunner
            .SaveChangesCallCount.Should()
            .Be(0, "saving through the runner would commit everything else the run tracked");
        _fakeEffectRunner.UpdateCallCount.Should().Be(0);
    }

    [Test]
    public async Task BeforeJunctionExecution_NullMetadata_ReturnsWithoutError()
    {
        // Arrange — train with null metadata (no reflection call)
        var train = new TestTrain();
        train.EffectRunner = _fakeEffectRunner;
        var junction = CreateTestJunction("SomeJunction");

        // Act & Assert
        var act = () => _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);
        await act.Should().NotThrowAsync();

        _fakeEffectRunner.UpdateCallCount.Should().Be(0);
    }

    [Test]
    public async Task BeforeJunctionExecution_NullEffectRunner_ReturnsWithoutError()
    {
        // Arrange — train with metadata but no EffectRunner
        var train = new TestTrain();
        SetInternalProperty(train, "Metadata", CreateMetadata());
        // EffectRunner left as null
        var junction = CreateTestJunction("SomeJunction");

        // Act & Assert
        var act = () => _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task BeforeJunctionExecution_ACancelledCallerStopsWaiting_AndTheRunSettlesTheWrite()
    {
        // The write carries every caller's change, so no one caller's token may cancel it. The
        // caller stops waiting; the run settles the write before its terminal write.
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");
        var gate = _factory.Hold();
        using var cts = new CancellationTokenSource();

        var before = _provider.BeforeJunctionExecution(junction, train, cts.Token);
        await _factory.Entered.WaitAsync(TimeSpan.FromSeconds(30));
        await cts.CancelAsync();

        await FluentActions
            .Awaiting(() => before)
            .Should()
            .ThrowAsync<OperationCanceledException>();

        var settled = _provider.Settle();
        settled.IsCompleted.Should().BeFalse("the write the caller left is still in flight");

        gate.SetResult();
        await settled;
        _factory.LastToken.Should().Be(CancellationToken.None);
        (await StoredRow(train)).CurrentlyRunningJunction.Should().Be("ProcessDataJunction");
    }

    [Test]
    public async Task BeforeJunctionExecution_ConcurrentBranchesShareAWrite_RatherThanQueueForOne()
    {
        // Eight branches start a junction while a write is in flight. They wait for that write and
        // the one after it, which carries all of their changes, not for eight writes in turn.
        var train = CreateTestTrain();
        var gate = _factory.Hold();

        var first = _provider.BeforeJunctionExecution(
            CreateTestJunction("Branch0"),
            train,
            CancellationToken.None
        );
        await _factory.Entered.WaitAsync(TimeSpan.FromSeconds(30));

        var others = Enumerable
            .Range(1, 7)
            .Select(i =>
                _provider.BeforeJunctionExecution(
                    CreateTestJunction($"Branch{i}"),
                    train,
                    CancellationToken.None
                )
            )
            .ToList();

        gate.SetResult();
        await Task.WhenAll([first, .. others]);

        _factory
            .Writes.Should()
            .Be(2, "the seven changes made while the first write was in flight share one write");
        (await StoredRow(train)).CurrentlyRunningJunction.Should().Be("Branch7");
    }

    [Test]
    public async Task BeforeJunctionExecution_ARowNotSavedYet_WritesNothing()
    {
        var train = CreateTestTrain(metadata: CreateMetadata(), save: false);
        var junction = CreateTestJunction("SomeJunction");

        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        _factory.Writes.Should().Be(0);
        train.Metadata!.CurrentlyRunningJunction.Should().Be("SomeJunction");
    }

    #endregion

    #region AfterJunctionExecution Tests

    [Test]
    public async Task AfterJunctionExecution_ClearsCurrentlyRunningJunction()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");
        train.Metadata!.CurrentlyRunningJunction = "ProcessDataJunction";
        train.Metadata!.JunctionStartedAt = DateTime.UtcNow;

        // Act
        await _provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        // Assert
        train.Metadata!.CurrentlyRunningJunction.Should().BeNull();
    }

    [Test]
    public async Task AfterJunctionExecution_ClearsJunctionStartedAt()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");
        train.Metadata!.CurrentlyRunningJunction = "ProcessDataJunction";
        train.Metadata!.JunctionStartedAt = DateTime.UtcNow;

        // Act
        await _provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        // Assert
        train.Metadata!.JunctionStartedAt.Should().BeNull();
    }

    [Test]
    public async Task AfterJunctionExecution_ClearsTheProgressColumnsOnTheRow()
    {
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");
        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        await _provider.AfterJunctionExecution(junction, train, CancellationToken.None);
        await _provider.Settle();

        var row = await StoredRow(train);
        row.CurrentlyRunningJunction.Should().BeNull();
        row.JunctionStartedAt.Should().BeNull();
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(0);
        _fakeEffectRunner.UpdateCallCount.Should().Be(0);
    }

    [Test]
    public async Task AfterJunctionExecution_DoesNotPassTheCallersToken()
    {
        // The junction's work has returned; a cancelled caller must not cancel the write that
        // records it, or the run is recorded Cancelled although the work finished.
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await _provider.AfterJunctionExecution(junction, train, cts.Token);
        await _provider.Settle();

        _factory.LastToken.Should().Be(CancellationToken.None);
    }

    [Test]
    public async Task AfterJunctionExecution_AFailingWrite_DoesNotReplaceTheJunctionsResult()
    {
        var train = CreateTestTrain();
        var junction = CreateTestJunction("ProcessDataJunction");
        _factory.Throws = true;

        var act = () => _provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        await act.Should()
            .NotThrowAsync(
                "a failed progress write after finished work is logged, not the outcome"
            );
        await FluentActions
            .Awaiting(() => _provider.Settle())
            .Should()
            .NotThrowAsync("the run's terminal write follows whatever the writer did");
        train.Metadata!.CurrentlyRunningJunction.Should().BeNull();
    }

    [Test]
    public async Task AfterJunctionExecution_NullMetadata_ReturnsWithoutError()
    {
        // Arrange — train with null metadata (no reflection call)
        var train = new TestTrain();
        train.EffectRunner = _fakeEffectRunner;
        var junction = CreateTestJunction("SomeJunction");

        // Act & Assert
        var act = () => _provider.AfterJunctionExecution(junction, train, CancellationToken.None);
        await act.Should().NotThrowAsync();

        _fakeEffectRunner.UpdateCallCount.Should().Be(0);
    }

    #endregion

    #region Full Lifecycle Tests

    [Test]
    public async Task FullLifecycle_BeforeAndAfter_SetsAndClearsJunctionProgress()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("FetchDataJunction");

        // Act — Before
        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        // Assert — progress is set
        train.Metadata!.CurrentlyRunningJunction.Should().Be("FetchDataJunction");
        train.Metadata!.JunctionStartedAt.Should().NotBeNull();

        // Act — After
        await _provider.AfterJunctionExecution(junction, train, CancellationToken.None);
        await _provider.Settle();

        // Assert — progress is cleared
        train.Metadata!.CurrentlyRunningJunction.Should().BeNull();
        train.Metadata!.JunctionStartedAt.Should().BeNull();
    }

    [Test]
    public async Task FullLifecycle_MultipleJunctions_TracksEachJunctionSeparately()
    {
        // Arrange
        var (train, _) = CreateTestTrainAndJunction("Unused");
        var junction1 = CreateTestJunction("Junction1");
        var junction2 = CreateTestJunction("Junction2");

        // Step 1 lifecycle
        await _provider.BeforeJunctionExecution(junction1, train, CancellationToken.None);
        train.Metadata!.CurrentlyRunningJunction.Should().Be("Junction1");

        await _provider.AfterJunctionExecution(junction1, train, CancellationToken.None);
        await _provider.Settle();
        train.Metadata!.CurrentlyRunningJunction.Should().BeNull();

        // Step 2 lifecycle
        await _provider.BeforeJunctionExecution(junction2, train, CancellationToken.None);
        train.Metadata!.CurrentlyRunningJunction.Should().Be("Junction2");

        await _provider.AfterJunctionExecution(junction2, train, CancellationToken.None);
        await _provider.Settle();
        train.Metadata!.CurrentlyRunningJunction.Should().BeNull();

        // Four writes of the two columns (before and after each junction, each settled before the
        // next), none through the runner.
        _factory.Writes.Should().Be(4);
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(0);
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_DoesNotThrow()
    {
        var act = () => _provider.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var act = () =>
        {
            _provider.Dispose();
            _provider.Dispose();
        };
        act.Should().NotThrow();
    }

    #endregion

    #region Test Helpers

    private (TestTrain train, TestJunction junction) CreateTestTrainAndJunction(string junctionName)
    {
        var train = CreateTestTrain();
        var junction = CreateTestJunction(junctionName);
        return (train, junction);
    }

    private TestTrain CreateTestTrain(Metadata? metadata = null, bool save = true)
    {
        var train = new TestTrain();

        // EffectRunner has a public setter
        train.EffectRunner = _fakeEffectRunner;

        // Metadata has an internal setter — use reflection. Saved first, as a run's row is before
        // its first junction, so the provider has a row to write to.
        var metadataToSet = metadata ?? CreateMetadata();
        if (save)
        {
            using var context = (IDataContext)_factory.Create();
            context.Metadatas.Add(metadataToSet);
            context.SaveChanges(CancellationToken.None).GetAwaiter().GetResult();
        }
        SetInternalProperty(train, "Metadata", metadataToSet);

        return train;
    }

    private async Task<Metadata> StoredRow(TestTrain train)
    {
        using var context = (IDataContext)_factory.Create();
        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == train.Metadata!.Id);
    }

    /// <summary>
    /// The real in-memory factory, counting the contexts the provider opens to write and the
    /// token it opens them with, failing on request, and holding a write until released.
    /// </summary>
    private sealed class RecordingFactory(IDataContextProviderFactory inner)
        : IDataContextProviderFactory
    {
        private int _writes;
        private TaskCompletionSource? _gate;
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int Writes => _writes;
        public CancellationToken LastToken { get; private set; }
        public bool Throws { get; set; }

        /// <summary>Completes once a write has started.</summary>
        public Task Entered => _entered.Task;

        /// <summary>Holds every write until the returned source is completed.</summary>
        public TaskCompletionSource Hold() =>
            _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEffectProvider Create() => inner.Create();

        public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
        {
            if (Throws)
                throw new InvalidOperationException("database unavailable");

            Interlocked.Increment(ref _writes);
            LastToken = cancellationToken;
            _entered.TrySetResult();
            if (_gate is { } gate)
                await gate.Task;
            return await inner.CreateDbContextAsync(cancellationToken);
        }
    }

    private TestTrain CreateTestTrain(bool withNullMetadata)
    {
        var train = new TestTrain();
        train.EffectRunner = _fakeEffectRunner;
        // Leave Metadata as null (default)
        return train;
    }

    private TestJunction CreateTestJunction(string name)
    {
        var junction = new TestJunction();
        SetJunctionMetadata(junction, name);
        return junction;
    }

    private static Metadata CreateMetadata() =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

    /// <summary>
    /// Sets a property with an internal setter via reflection.
    /// </summary>
    private static void SetInternalProperty<T>(T target, string propertyName, object? value)
    {
        var prop = typeof(T).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        prop?.SetValue(target, value);
    }

    /// <summary>
    /// Sets the JunctionMetadata on an EffectJunction via reflection (private setter).
    /// </summary>
    private static void SetJunctionMetadata(TestJunction junction, string name)
    {
        var parentMetadata = CreateMetadata();

        var junctionMeta = JunctionMetadata.Create(
            new CreateJunctionMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                InputType = typeof(string),
                OutputType = typeof(string),
                State = EitherStatus.IsRight,
            },
            parentMetadata
        );

        var prop = typeof(EffectJunction<string, string>).GetProperty(
            "Metadata",
            BindingFlags.Public | BindingFlags.Instance
        );
        prop?.SetValue(junction, junctionMeta);
    }

    /// <summary>
    /// Concrete test double for EffectTrain. Only used for property access in tests.
    /// </summary>
    private class TestTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<PassThrough1_263>().Resolve();

        private sealed class PassThrough1_263 : Junction<string, string>
        {
            public override Task<string> Run(string input) => Task.FromResult(input);
        }
    }

    /// <summary>
    /// Concrete test double for EffectJunction.
    /// </summary>
    private class TestJunction : EffectJunction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    /// <summary>
    /// Fake IEffectRunner that tracks method calls for assertions.
    /// </summary>
    private class FakeEffectRunner : IEffectRunner
    {
        public int UpdateCallCount { get; private set; }
        public int SaveChangesCallCount { get; private set; }
        public int TrackCallCount { get; private set; }
        public CancellationToken LastSaveChangesCancellationToken { get; private set; }

        public Task SaveChanges(CancellationToken cancellationToken)
        {
            SaveChangesCallCount++;
            LastSaveChangesCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task Track(IModel model)
        {
            TrackCallCount++;
            return Task.CompletedTask;
        }

        public Task Update(IModel model)
        {
            UpdateCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    #endregion
}
