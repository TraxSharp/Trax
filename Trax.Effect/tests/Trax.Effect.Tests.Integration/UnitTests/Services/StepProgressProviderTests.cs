using System.Reflection;
using FluentAssertions;
using LanguageExt;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.StepMetadata;
using Trax.Effect.Models.StepMetadata.DTOs;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StepProvider.Progress.Services.StepProgressProvider;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="StepProgressProvider"/>, the step effect provider
/// that writes currently-running step name and timestamp to workflow metadata.
/// </summary>
[TestFixture]
public class StepProgressProviderTests
{
    private StepProgressProvider _provider;
    private FakeEffectRunner _fakeEffectRunner;

    [SetUp]
    public void SetUp()
    {
        _provider = new StepProgressProvider();
        _fakeEffectRunner = new FakeEffectRunner();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _fakeEffectRunner.Dispose();
    }

    #region BeforeStepExecution Tests

    [Test]
    public async Task BeforeStepExecution_SetsCurrentlyRunningStep()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");

        // Act
        await _provider.BeforeStepExecution(step, workflow, CancellationToken.None);

        // Assert
        workflow.Metadata!.CurrentlyRunningStep.Should().Be("ProcessDataStep");
    }

    [Test]
    public async Task BeforeStepExecution_SetsStepStartedAt()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");
        var before = DateTime.UtcNow;

        // Act
        await _provider.BeforeStepExecution(step, workflow, CancellationToken.None);

        // Assert
        workflow.Metadata!.StepStartedAt.Should().NotBeNull();
        workflow.Metadata!.StepStartedAt.Should().BeOnOrAfter(before);
        workflow.Metadata!.StepStartedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Test]
    public async Task BeforeStepExecution_CallsEffectRunnerUpdate()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");

        // Act
        await _provider.BeforeStepExecution(step, workflow, CancellationToken.None);

        // Assert
        _fakeEffectRunner.UpdateCallCount.Should().Be(1);
    }

    [Test]
    public async Task BeforeStepExecution_CallsEffectRunnerSaveChanges()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");

        // Act
        await _provider.BeforeStepExecution(step, workflow, CancellationToken.None);

        // Assert
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(1);
    }

    [Test]
    public async Task BeforeStepExecution_NullMetadata_ReturnsWithoutError()
    {
        // Arrange — workflow with null metadata (no reflection call)
        var workflow = new TestWorkflow();
        workflow.EffectRunner = _fakeEffectRunner;
        var step = CreateTestStep("SomeStep");

        // Act & Assert
        var act = () => _provider.BeforeStepExecution(step, workflow, CancellationToken.None);
        await act.Should().NotThrowAsync();

        _fakeEffectRunner.UpdateCallCount.Should().Be(0);
    }

    [Test]
    public async Task BeforeStepExecution_NullEffectRunner_ReturnsWithoutError()
    {
        // Arrange — workflow with metadata but no EffectRunner
        var workflow = new TestWorkflow();
        SetInternalProperty(workflow, "Metadata", CreateMetadata());
        // EffectRunner left as null
        var step = CreateTestStep("SomeStep");

        // Act & Assert
        var act = () => _provider.BeforeStepExecution(step, workflow, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task BeforeStepExecution_PassesCancellationTokenToSaveChanges()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");
        using var cts = new CancellationTokenSource();

        // Act
        await _provider.BeforeStepExecution(step, workflow, cts.Token);

        // Assert
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(1);
        _fakeEffectRunner.LastSaveChangesCancellationToken.Should().Be(cts.Token);
    }

    #endregion

    #region AfterStepExecution Tests

    [Test]
    public async Task AfterStepExecution_ClearsCurrentlyRunningStep()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");
        workflow.Metadata!.CurrentlyRunningStep = "ProcessDataStep";
        workflow.Metadata!.StepStartedAt = DateTime.UtcNow;

        // Act
        await _provider.AfterStepExecution(step, workflow, CancellationToken.None);

        // Assert
        workflow.Metadata!.CurrentlyRunningStep.Should().BeNull();
    }

    [Test]
    public async Task AfterStepExecution_ClearsStepStartedAt()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");
        workflow.Metadata!.CurrentlyRunningStep = "ProcessDataStep";
        workflow.Metadata!.StepStartedAt = DateTime.UtcNow;

        // Act
        await _provider.AfterStepExecution(step, workflow, CancellationToken.None);

        // Assert
        workflow.Metadata!.StepStartedAt.Should().BeNull();
    }

    [Test]
    public async Task AfterStepExecution_CallsEffectRunnerUpdateAndSaveChanges()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("ProcessDataStep");

        // Act
        await _provider.AfterStepExecution(step, workflow, CancellationToken.None);

        // Assert
        _fakeEffectRunner.UpdateCallCount.Should().Be(1);
        _fakeEffectRunner.SaveChangesCallCount.Should().Be(1);
    }

    [Test]
    public async Task AfterStepExecution_NullMetadata_ReturnsWithoutError()
    {
        // Arrange — workflow with null metadata (no reflection call)
        var workflow = new TestWorkflow();
        workflow.EffectRunner = _fakeEffectRunner;
        var step = CreateTestStep("SomeStep");

        // Act & Assert
        var act = () => _provider.AfterStepExecution(step, workflow, CancellationToken.None);
        await act.Should().NotThrowAsync();

        _fakeEffectRunner.UpdateCallCount.Should().Be(0);
    }

    #endregion

    #region Full Lifecycle Tests

    [Test]
    public async Task FullLifecycle_BeforeAndAfter_SetsAndClearsStepProgress()
    {
        // Arrange
        var (workflow, step) = CreateTestWorkflowAndStep("FetchDataStep");

        // Act — Before
        await _provider.BeforeStepExecution(step, workflow, CancellationToken.None);

        // Assert — progress is set
        workflow.Metadata!.CurrentlyRunningStep.Should().Be("FetchDataStep");
        workflow.Metadata!.StepStartedAt.Should().NotBeNull();

        // Act — After
        await _provider.AfterStepExecution(step, workflow, CancellationToken.None);

        // Assert — progress is cleared
        workflow.Metadata!.CurrentlyRunningStep.Should().BeNull();
        workflow.Metadata!.StepStartedAt.Should().BeNull();
    }

    [Test]
    public async Task FullLifecycle_MultipleSteps_TracksEachStepSeparately()
    {
        // Arrange
        var (workflow, _) = CreateTestWorkflowAndStep("Unused");
        var step1 = CreateTestStep("Step1");
        var step2 = CreateTestStep("Step2");

        // Step 1 lifecycle
        await _provider.BeforeStepExecution(step1, workflow, CancellationToken.None);
        workflow.Metadata!.CurrentlyRunningStep.Should().Be("Step1");

        await _provider.AfterStepExecution(step1, workflow, CancellationToken.None);
        workflow.Metadata!.CurrentlyRunningStep.Should().BeNull();

        // Step 2 lifecycle
        await _provider.BeforeStepExecution(step2, workflow, CancellationToken.None);
        workflow.Metadata!.CurrentlyRunningStep.Should().Be("Step2");

        await _provider.AfterStepExecution(step2, workflow, CancellationToken.None);
        workflow.Metadata!.CurrentlyRunningStep.Should().BeNull();

        // Verify 4 updates (before+after for each step) and 4 saves
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

    private (TestWorkflow workflow, TestStep step) CreateTestWorkflowAndStep(string stepName)
    {
        var workflow = CreateTestWorkflow();
        var step = CreateTestStep(stepName);
        return (workflow, step);
    }

    private TestWorkflow CreateTestWorkflow(
        Metadata? metadata = null,
        IEffectRunner? effectRunner = null
    )
    {
        var workflow = new TestWorkflow();

        // EffectRunner has a public setter
        workflow.EffectRunner = effectRunner ?? _fakeEffectRunner;

        // Metadata has an internal setter — use reflection
        var metadataToSet = metadata ?? CreateMetadata();
        SetInternalProperty(workflow, "Metadata", metadataToSet);

        return workflow;
    }

    private TestWorkflow CreateTestWorkflow(bool withNullMetadata)
    {
        var workflow = new TestWorkflow();
        workflow.EffectRunner = _fakeEffectRunner;
        // Leave Metadata as null (default)
        return workflow;
    }

    private TestStep CreateTestStep(string name)
    {
        var step = new TestStep();
        SetStepMetadata(step, name);
        return step;
    }

    private static Metadata CreateMetadata() =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = "TestWorkflow",
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
    /// Sets the StepMetadata on an EffectStep via reflection (private setter).
    /// </summary>
    private static void SetStepMetadata(TestStep step, string name)
    {
        var parentMetadata = CreateMetadata();

        var stepMeta = StepMetadata.Create(
            new CreateStepMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                InputType = typeof(string),
                OutputType = typeof(string),
                State = EitherStatus.IsRight,
            },
            parentMetadata
        );

        var prop = typeof(EffectStep<string, string>).GetProperty(
            "Metadata",
            BindingFlags.Public | BindingFlags.Instance
        );
        prop?.SetValue(step, stepMeta);
    }

    /// <summary>
    /// Concrete test double for EffectWorkflow. Only used for property access in tests.
    /// </summary>
    private class TestWorkflow : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Task.FromResult<Either<Exception, string>>(input);
    }

    /// <summary>
    /// Concrete test double for EffectStep.
    /// </summary>
    private class TestStep : EffectStep<string, string>
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
