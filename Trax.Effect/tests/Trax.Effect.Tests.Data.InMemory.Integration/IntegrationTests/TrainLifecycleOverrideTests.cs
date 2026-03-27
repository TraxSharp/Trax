using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

public class TrainLifecycleOverrideTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<IRecordingTrain, RecordingTrain>()
            .AddScopedTraxRoute<IFailingRecordingTrain, FailingRecordingTrain>()
            .AddScopedTraxRoute<ICancellingRecordingTrain, CancellingRecordingTrain>()
            .AddScopedTraxRoute<IThrowingHookTrain, ThrowingHookTrain>()
            .AddScopedTraxRoute<IThrowingOnFailedHookTrain, ThrowingOnFailedHookTrain>()
            .AddScopedTraxRoute<IThrowingOnCancelledHookTrain, ThrowingOnCancelledHookTrain>()
            .AddScopedTraxRoute<IPartialOverrideTrain, PartialOverrideTrain>()
            .AddScopedTraxRoute<INoOverrideTrain, NoOverrideTrain>()
            .AddScopedTraxRoute<IOutputRecordingTrain, OutputRecordingTrain>()
            .AddScopedTraxRoute<ITypedAccessTrain, TypedAccessTrain>()
            .AddScopedTraxRoute<IFailingTypedAccessTrain, FailingTypedAccessTrain>()
            .BuildServiceProvider();

    #region OnStarted

    [Test]
    public async Task Run_SuccessfulTrain_CallsOnStarted()
    {
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();

        await train.Run(Unit.Default);

        train.StartedCalled.Should().BeTrue();
        train.StartedMetadata.Should().NotBeNull();
        train.StartedMetadata!.Name.Should().Be(typeof(IRecordingTrain).FullName);
    }

    [Test]
    public async Task Run_FailingTrain_StillCallsOnStarted()
    {
        var train = (FailingRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IFailingRecordingTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<TrainException>();

        train.StartedCalled.Should().BeTrue();
    }

    [Test]
    public async Task Run_OnStartedThrows_TrainStillRuns()
    {
        var train = (ThrowingHookTrain)
            Scope.ServiceProvider.GetRequiredService<IThrowingHookTrain>();

        var result = await train.Run(Unit.Default);

        result.Should().Be(Unit.Default);
        train.Metadata!.TrainState.Should().Be(Enums.TrainState.Completed);
    }

    #endregion

    #region OnCompleted

    [Test]
    public async Task Run_SuccessfulTrain_CallsOnCompleted()
    {
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();

        await train.Run(Unit.Default);

        train.CompletedCalled.Should().BeTrue();
        train.CompletedMetadata.Should().NotBeNull();
    }

    [Test]
    public async Task Run_FailingTrain_DoesNotCallOnCompleted()
    {
        var train = (FailingRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IFailingRecordingTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<TrainException>();

        train.CompletedCalled.Should().BeFalse();
    }

    [Test]
    public async Task Run_OnCompletedThrows_DoesNotCauseFailure()
    {
        var train = (ThrowingHookTrain)
            Scope.ServiceProvider.GetRequiredService<IThrowingHookTrain>();

        var act = async () => await train.Run(Unit.Default);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region OnFailed

    [Test]
    public async Task Run_FailingTrain_CallsOnFailedWithException()
    {
        var train = (FailingRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IFailingRecordingTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<TrainException>();

        train.FailedCalled.Should().BeTrue();
        train.FailedException.Should().NotBeNull();
        train.FailedException.Should().BeOfType<TrainException>();
        train.FailedMetadata.Should().NotBeNull();
    }

    [Test]
    public async Task Run_SuccessfulTrain_DoesNotCallOnFailed()
    {
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();

        await train.Run(Unit.Default);

        train.FailedCalled.Should().BeFalse();
    }

    [Test]
    public async Task Run_OnFailedThrows_OriginalExceptionStillPropagates()
    {
        var train = (ThrowingOnFailedHookTrain)
            Scope.ServiceProvider.GetRequiredService<IThrowingOnFailedHookTrain>();

        var act = async () => await train.Run(Unit.Default);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Intentional train failure*");
    }

    #endregion

    #region OnCancelled

    [Test]
    public async Task Run_CancelledTrain_CallsOnCancelled()
    {
        var train = (CancellingRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<ICancellingRecordingTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<OperationCanceledException>();

        train.CancelledCalled.Should().BeTrue();
        train.CancelledMetadata.Should().NotBeNull();
    }

    [Test]
    public async Task Run_FailingTrain_NonCancellation_DoesNotCallOnCancelled()
    {
        var train = (FailingRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IFailingRecordingTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<TrainException>();

        train.CancelledCalled.Should().BeFalse();
    }

    [Test]
    public async Task Run_OnCancelledThrows_CancellationStillPropagates()
    {
        var train = (ThrowingOnCancelledHookTrain)
            Scope.ServiceProvider.GetRequiredService<IThrowingOnCancelledHookTrain>();

        var act = async () => await train.Run(Unit.Default);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Ordering & Defaults

    [Test]
    public async Task Run_SuccessfulTrain_OnStartedBeforeOnCompleted()
    {
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();

        await train.Run(Unit.Default);

        train.CallOrder.Should().ContainInOrder("OnStarted", "OnCompleted");
    }

    [Test]
    public async Task Run_FailingTrain_OnStartedBeforeOnFailed()
    {
        var train = (FailingRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IFailingRecordingTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<TrainException>();

        train.CallOrder.Should().ContainInOrder("OnStarted", "OnFailed");
    }

    [Test]
    public async Task Run_NoOverrides_DefaultsDoNotThrow()
    {
        var train = Scope.ServiceProvider.GetRequiredService<INoOverrideTrain>();

        var act = async () => await train.Run(Unit.Default);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_PartialOverride_OnlyOverriddenMethodCalled()
    {
        var train = (PartialOverrideTrain)
            Scope.ServiceProvider.GetRequiredService<IPartialOverrideTrain>();

        await train.Run(Unit.Default);

        train.CompletedCalled.Should().BeTrue();
        train.CallOrder.Should().ContainSingle().Which.Should().Be("OnCompleted");
    }

    [Test]
    public async Task Run_CancellationTokenPassedToHooks()
    {
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();
        using var cts = new CancellationTokenSource();

        await train.Run(Unit.Default, cts.Token);

        train.StartedCancellationToken.Should().Be(cts.Token);
        train.CompletedCancellationToken.Should().Be(cts.Token);
    }

    [Test]
    public async Task Run_WithMetadataAndCancellationToken_TokenPreservedThroughHooks()
    {
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();
        using var cts = new CancellationTokenSource();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IRecordingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await train.Run(Unit.Default, metadata, cts.Token);

        train.StartedCancellationToken.Should().Be(cts.Token);
        train.CompletedCancellationToken.Should().Be(cts.Token);
    }

    [Test]
    public async Task Run_WithMetadataAndCancellationToken_TokenNotOverwrittenByDefault()
    {
        // Regression test: previously Run(input, metadata, ct) called Run(input)
        // which overwrote the token with CancellationToken.None
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();
        using var cts = new CancellationTokenSource();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IRecordingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await train.Run(Unit.Default, metadata, cts.Token);

        train
            .StartedCancellationToken.Should()
            .NotBe(
                CancellationToken.None,
                "the real token should survive through to hooks, not be overwritten with default"
            );
    }

    [Test]
    public async Task Run_WithMetadataOnly_DoesNotClobberToken()
    {
        // Run(input, metadata) should pass CancellationToken through without issue
        var train = (RecordingTrain)Scope.ServiceProvider.GetRequiredService<IRecordingTrain>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IRecordingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await train.Run(Unit.Default, metadata);

        train.StartedCalled.Should().BeTrue();
        train.CompletedCalled.Should().BeTrue();
    }

    [Test]
    public async Task Run_CancelledDuringExecution_WithMetadataOverload_CallsOnCancelled()
    {
        var train = (CancellingRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<ICancellingRecordingTrain>();
        using var cts = new CancellationTokenSource();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ICancellingRecordingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        var act = async () => await train.Run(Unit.Default, metadata, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        train.CancelledCalled.Should().BeTrue();
    }

    #endregion

    #region OutputSerialization

    [Test]
    public async Task Run_WithoutSaveTrainParameters_OnCompletedHasSerializedOutput()
    {
        var train = (OutputRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IOutputRecordingTrain>();

        await train.Run("test-input");

        train.CompletedCalled.Should().BeTrue();
        train.CapturedOutput.Should().NotBeNull();
    }

    [Test]
    public async Task Run_WithoutSaveTrainParameters_OutputContainsCorrectData()
    {
        var train = (OutputRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IOutputRecordingTrain>();

        await train.Run("test-input");

        var deserialized = JsonSerializer.Deserialize<TestOutputDto>(train.CapturedOutput!);
        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be("processed:test-input");
        deserialized.Count.Should().Be(42);
    }

    [Test]
    public async Task Run_WithoutSaveTrainParameters_GetOutputObjectStillAvailable()
    {
        var train = (OutputRecordingTrain)
            Scope.ServiceProvider.GetRequiredService<IOutputRecordingTrain>();

        await train.Run("test-input");

        object? outputObj = train.CapturedOutputObject;
        outputObj.Should().NotBeNull();
        var outputDto = outputObj as TestOutputDto;
        outputDto.Should().NotBeNull();
        outputDto!.Value.Should().Be("processed:test-input");
    }

    #endregion

    #region TypedAccess

    [Test]
    public async Task Run_OnCompleted_TrainInputIsTyped()
    {
        var train = (TypedAccessTrain)Scope.ServiceProvider.GetRequiredService<ITypedAccessTrain>();

        await train.Run("hello");

        train.CapturedInput.Should().Be("hello");
    }

    [Test]
    public async Task Run_OnCompleted_TrainOutputIsTyped()
    {
        var train = (TypedAccessTrain)Scope.ServiceProvider.GetRequiredService<ITypedAccessTrain>();

        await train.Run("hello");

        train.CapturedOutput.Should().NotBeNull();
        train.CapturedOutput!.Value.Should().Be("processed:hello");
        train.CapturedOutput!.Count.Should().Be(42);
    }

    [Test]
    public async Task Run_OnFailed_TrainInputIsTyped()
    {
        var train = (FailingTypedAccessTrain)
            Scope.ServiceProvider.GetRequiredService<IFailingTypedAccessTrain>();

        var act = async () => await train.Run("hello");
        await act.Should().ThrowAsync<TrainException>();

        train.CapturedInput.Should().Be("hello");
    }

    [Test]
    public async Task Run_OnFailed_TrainOutputIsDefault()
    {
        var train = (FailingTypedAccessTrain)
            Scope.ServiceProvider.GetRequiredService<IFailingTypedAccessTrain>();

        var act = async () => await train.Run("hello");
        await act.Should().ThrowAsync<TrainException>();

        train.CapturedOutput.Should().BeNull();
    }

    [Test]
    public async Task Run_MetadataGetInput_ReturnsTyped()
    {
        var train = (TypedAccessTrain)Scope.ServiceProvider.GetRequiredService<ITypedAccessTrain>();

        await train.Run("hello");

        train.CapturedMetadataInput.Should().Be("hello");
    }

    [Test]
    public async Task Run_MetadataGetOutput_ReturnsTyped()
    {
        var train = (TypedAccessTrain)Scope.ServiceProvider.GetRequiredService<ITypedAccessTrain>();

        await train.Run("hello");

        train.CapturedMetadataOutput.Should().NotBeNull();
        train.CapturedMetadataOutput!.Value.Should().Be("processed:hello");
    }

    [Test]
    public async Task Run_MetadataGetInput_WrongType_ReturnsDefault()
    {
        var train = (TypedAccessTrain)Scope.ServiceProvider.GetRequiredService<ITypedAccessTrain>();

        await train.Run("hello");

        train.CapturedWrongTypeInput.Should().Be(0);
    }

    #endregion

    #region Test Trains

    private interface IRecordingTrain : IServiceTrain<Unit, Unit> { }

    private class RecordingTrain : ServiceTrain<Unit, Unit>, IRecordingTrain
    {
        public bool StartedCalled { get; private set; }
        public bool CompletedCalled { get; private set; }
        public bool FailedCalled { get; private set; }
        public bool CancelledCalled { get; private set; }
        public Metadata? StartedMetadata { get; private set; }
        public Metadata? CompletedMetadata { get; private set; }
        public Metadata? FailedMetadata { get; private set; }
        public Exception? FailedException { get; private set; }
        public Metadata? CancelledMetadata { get; private set; }
        public CancellationToken StartedCancellationToken { get; private set; }
        public CancellationToken CompletedCancellationToken { get; private set; }
        public List<string> CallOrder { get; } = [];

        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Activate(input).Resolve();

        protected override Task OnStarted(Metadata metadata, CancellationToken ct)
        {
            StartedCalled = true;
            StartedMetadata = metadata;
            StartedCancellationToken = ct;
            CallOrder.Add("OnStarted");
            return Task.CompletedTask;
        }

        protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            CompletedCalled = true;
            CompletedMetadata = metadata;
            CompletedCancellationToken = ct;
            CallOrder.Add("OnCompleted");
            return Task.CompletedTask;
        }

        protected override Task OnFailed(
            Metadata metadata,
            Exception exception,
            CancellationToken ct
        )
        {
            FailedCalled = true;
            FailedMetadata = metadata;
            FailedException = exception;
            CallOrder.Add("OnFailed");
            return Task.CompletedTask;
        }

        protected override Task OnCancelled(Metadata metadata, CancellationToken ct)
        {
            CancelledCalled = true;
            CancelledMetadata = metadata;
            CallOrder.Add("OnCancelled");
            return Task.CompletedTask;
        }
    }

    private interface IFailingRecordingTrain : IServiceTrain<Unit, Unit> { }

    private class FailingRecordingTrain : ServiceTrain<Unit, Unit>, IFailingRecordingTrain
    {
        public bool StartedCalled { get; private set; }
        public bool CompletedCalled { get; private set; }
        public bool FailedCalled { get; private set; }
        public bool CancelledCalled { get; private set; }
        public Metadata? FailedMetadata { get; private set; }
        public Exception? FailedException { get; private set; }
        public List<string> CallOrder { get; } = [];

        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            new TrainException("Intentional train failure");

        protected override Task OnStarted(Metadata metadata, CancellationToken ct)
        {
            StartedCalled = true;
            CallOrder.Add("OnStarted");
            return Task.CompletedTask;
        }

        protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            CompletedCalled = true;
            CallOrder.Add("OnCompleted");
            return Task.CompletedTask;
        }

        protected override Task OnFailed(
            Metadata metadata,
            Exception exception,
            CancellationToken ct
        )
        {
            FailedCalled = true;
            FailedMetadata = metadata;
            FailedException = exception;
            CallOrder.Add("OnFailed");
            return Task.CompletedTask;
        }

        protected override Task OnCancelled(Metadata metadata, CancellationToken ct)
        {
            CancelledCalled = true;
            CallOrder.Add("OnCancelled");
            return Task.CompletedTask;
        }
    }

    private interface ICancellingRecordingTrain : IServiceTrain<Unit, Unit> { }

    private class CancellingRecordingTrain : ServiceTrain<Unit, Unit>, ICancellingRecordingTrain
    {
        public bool CancelledCalled { get; private set; }
        public Metadata? CancelledMetadata { get; private set; }

        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            throw new OperationCanceledException("Intentional cancellation");

        protected override Task OnCancelled(Metadata metadata, CancellationToken ct)
        {
            CancelledCalled = true;
            CancelledMetadata = metadata;
            return Task.CompletedTask;
        }
    }

    private interface IThrowingHookTrain : IServiceTrain<Unit, Unit> { }

    private class ThrowingHookTrain : ServiceTrain<Unit, Unit>, IThrowingHookTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Activate(input).Resolve();

        protected override Task OnStarted(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("OnStarted hook failed");

        protected override Task OnCompleted(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("OnCompleted hook failed");
    }

    private interface IThrowingOnFailedHookTrain : IServiceTrain<Unit, Unit> { }

    private class ThrowingOnFailedHookTrain : ServiceTrain<Unit, Unit>, IThrowingOnFailedHookTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            new TrainException("Intentional train failure");

        protected override Task OnFailed(
            Metadata metadata,
            Exception exception,
            CancellationToken ct
        ) => throw new InvalidOperationException("OnFailed hook failed");
    }

    private interface IThrowingOnCancelledHookTrain : IServiceTrain<Unit, Unit> { }

    private class ThrowingOnCancelledHookTrain
        : ServiceTrain<Unit, Unit>,
            IThrowingOnCancelledHookTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            throw new OperationCanceledException("Intentional cancellation");

        protected override Task OnCancelled(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("OnCancelled hook failed");
    }

    private interface IPartialOverrideTrain : IServiceTrain<Unit, Unit> { }

    private class PartialOverrideTrain : ServiceTrain<Unit, Unit>, IPartialOverrideTrain
    {
        public bool CompletedCalled { get; private set; }
        public List<string> CallOrder { get; } = [];

        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Activate(input).Resolve();

        protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            CompletedCalled = true;
            CallOrder.Add("OnCompleted");
            return Task.CompletedTask;
        }
    }

    private interface INoOverrideTrain : IServiceTrain<Unit, Unit> { }

    private class NoOverrideTrain : ServiceTrain<Unit, Unit>, INoOverrideTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Activate(input).Resolve();
    }

    private record TestOutputDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("value")] string Value,
        [property: System.Text.Json.Serialization.JsonPropertyName("count")] int Count
    );

    private interface IOutputRecordingTrain : IServiceTrain<string, TestOutputDto> { }

    private class OutputRecordingTrain : ServiceTrain<string, TestOutputDto>, IOutputRecordingTrain
    {
        public bool CompletedCalled { get; private set; }
        public string? CapturedOutput { get; private set; }
        public dynamic? CapturedOutputObject { get; private set; }

        protected override async Task<Either<Exception, TestOutputDto>> RunInternal(string input) =>
            new TestOutputDto($"processed:{input}", 42);

        protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            CompletedCalled = true;
            CapturedOutput = metadata.Output;
            CapturedOutputObject = metadata.GetOutputObject();
            return Task.CompletedTask;
        }
    }

    private interface ITypedAccessTrain : IServiceTrain<string, TestOutputDto> { }

    private class TypedAccessTrain : ServiceTrain<string, TestOutputDto>, ITypedAccessTrain
    {
        public string? CapturedInput { get; private set; }
        public TestOutputDto? CapturedOutput { get; private set; }
        public string? CapturedMetadataInput { get; private set; }
        public TestOutputDto? CapturedMetadataOutput { get; private set; }
        public int CapturedWrongTypeInput { get; private set; }

        protected override async Task<Either<Exception, TestOutputDto>> RunInternal(string input) =>
            new TestOutputDto($"processed:{input}", 42);

        protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            CapturedInput = TrainInput;
            CapturedOutput = TrainOutput;
            CapturedMetadataInput = metadata.GetInput<string>();
            CapturedMetadataOutput = metadata.GetOutput<TestOutputDto>();
            CapturedWrongTypeInput = metadata.GetInput<int>();
            return Task.CompletedTask;
        }
    }

    private interface IFailingTypedAccessTrain : IServiceTrain<string, TestOutputDto> { }

    private class FailingTypedAccessTrain
        : ServiceTrain<string, TestOutputDto>,
            IFailingTypedAccessTrain
    {
        public string? CapturedInput { get; private set; }
        public TestOutputDto? CapturedOutput { get; private set; }

        protected override async Task<Either<Exception, TestOutputDto>> RunInternal(string input) =>
            new TrainException("Intentional failure");

        protected override Task OnFailed(
            Metadata metadata,
            Exception exception,
            CancellationToken ct
        )
        {
            CapturedInput = TrainInput;
            CapturedOutput = TrainOutput;
            return Task.CompletedTask;
        }
    }

    #endregion
}
