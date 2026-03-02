using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

#pragma warning disable CS8766 // Nullability mismatch on Metadata property inherited from EffectTrain

namespace Trax.Dashboard.Tests.Integration.Fakes;

// --- Manifest-compatible fakes for scheduler builder tests ---
// These satisfy TTrain : IServiceTrain<TInput, Unit> where TInput : IManifestProperties

public record FakeManifestInputA : IManifestProperties;

public interface IFakeSchedulerTrainA : IServiceTrain<FakeManifestInputA, Unit> { }

public class FakeSchedulerTrainA : ServiceTrain<FakeManifestInputA, Unit>, IFakeSchedulerTrainA
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputA input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

public record FakeManifestInputB : IManifestProperties;

public interface IFakeSchedulerTrainB : IServiceTrain<FakeManifestInputB, Unit> { }

public class FakeSchedulerTrainB : ServiceTrain<FakeManifestInputB, Unit>, IFakeSchedulerTrainB
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputB input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

public record FakeManifestInputC : IManifestProperties;

public interface IFakeSchedulerTrainC : IServiceTrain<FakeManifestInputC, Unit> { }

public class FakeSchedulerTrainC : ServiceTrain<FakeManifestInputC, Unit>, IFakeSchedulerTrainC
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputC input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

public record FakeManifestInputD : IManifestProperties;

public interface IFakeSchedulerTrainD : IServiceTrain<FakeManifestInputD, Unit> { }

public class FakeSchedulerTrainD : ServiceTrain<FakeManifestInputD, Unit>, IFakeSchedulerTrainD
{
    protected override Task<Either<Exception, Unit>> RunInternal(FakeManifestInputD input) =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}
