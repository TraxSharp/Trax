using System.Reflection;
using FluentAssertions;
using LanguageExt;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;
using Trax.Effect.Models;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="JunctionProgressProvider"/>, the junction effect provider
/// that writes currently-running junction name and timestamp to train metadata.
/// </summary>
[TestFixture]
public class JunctionProgressProviderTests
{
    private JunctionProgressProvider _provider;
    private FakeEffectRunner _fakeEffectRunner;

    [SetUp]
    public void SetUp()
    {
        _provider = new JunctionProgressProvider();
        _fakeEffectRunner = new FakeEffectRunner();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _fakeEffectRunner.Dispose();
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
    public async Task BeforeJunctionExecution_CallsEffectRunnerUpdate()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");

        // Act
        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        // Assert
        _fakeEffectRunner.UpdateCallCount.Should().Be(1);
    }

    [Test]
    public async Task BeforeJunctionExecution_CallsEffectRunnerSaveChanges()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");

        // Act
        await _provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        // Assert
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(1);
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
    public async Task BeforeJunctionExecution_PassesCancellationTokenToSaveChanges()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");
        using var cts = new CancellationTokenSource();

        // Act
        await _provider.BeforeJunctionExecution(junction, train, cts.Token);

        // Assert
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(1);
        _fakeEffectRunner.LastSaveChangesCancellationToken.Should().Be(cts.Token);
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
    public async Task AfterJunctionExecution_CallsEffectRunnerUpdateAndSaveChanges()
    {
        // Arrange
        var (train, junction) = CreateTestTrainAndJunction("ProcessDataJunction");

        // Act
        await _provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        // Assert
        _fakeEffectRunner.UpdateCallCount.Should().Be(1);
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(1);
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
        train.Metadata!.CurrentlyRunningJunction.Should().BeNull();

        // Step 2 lifecycle
        await _provider.BeforeJunctionExecution(junction2, train, CancellationToken.None);
        train.Metadata!.CurrentlyRunningJunction.Should().Be("Junction2");

        await _provider.AfterJunctionExecution(junction2, train, CancellationToken.None);
        train.Metadata!.CurrentlyRunningJunction.Should().BeNull();

        // Verify 4 updates (before+after for each junction) and 4 saves
        _fakeEffectRunner.UpdateCallCount.Should().Be(4);
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(4);
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

    private TestTrain CreateTestTrain(Metadata? metadata = null, IEffectRunner? effectRunner = null)
    {
        var train = new TestTrain();

        // EffectRunner has a public setter
        train.EffectRunner = effectRunner ?? _fakeEffectRunner;

        // Metadata has an internal setter — use reflection
        var metadataToSet = metadata ?? CreateMetadata();
        SetInternalProperty(train, "Metadata", metadataToSet);

        return train;
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
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Task.FromResult<Either<Exception, string>>(input);
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
