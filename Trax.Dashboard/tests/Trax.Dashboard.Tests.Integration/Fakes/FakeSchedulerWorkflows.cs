using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

#pragma warning disable CS8766 // Nullability mismatch on Metadata property inherited from EffectWorkflow

namespace Trax.Dashboard.Tests.Integration.Fakes;

// --- Manifest-compatible fakes for scheduler builder tests ---
// These satisfy TWorkflow : IServiceTrain<TInput, Unit> where TInput : IManifestProperties

public record FakeManifestInputA : IManifestProperties;

public interface IFakeSchedulerWorkflowA : IServiceTrain<FakeManifestInputA, Unit> { }

public class FakeSchedulerWorkflowA
    : ServiceTrain<FakeManifestInputA, Unit>,
        IFakeSchedulerWorkflowA
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputA input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

public record FakeManifestInputB : IManifestProperties;

public interface IFakeSchedulerWorkflowB : IServiceTrain<FakeManifestInputB, Unit> { }

public class FakeSchedulerWorkflowB
    : ServiceTrain<FakeManifestInputB, Unit>,
        IFakeSchedulerWorkflowB
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputB input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

public record FakeManifestInputC : IManifestProperties;

public interface IFakeSchedulerWorkflowC : IServiceTrain<FakeManifestInputC, Unit> { }

public class FakeSchedulerWorkflowC
    : ServiceTrain<FakeManifestInputC, Unit>,
        IFakeSchedulerWorkflowC
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputC input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

public record FakeManifestInputD : IManifestProperties;

public interface IFakeSchedulerWorkflowD : IServiceTrain<FakeManifestInputD, Unit> { }

public class FakeSchedulerWorkflowD
    : ServiceTrain<FakeManifestInputD, Unit>,
        IFakeSchedulerWorkflowD
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputD input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}
