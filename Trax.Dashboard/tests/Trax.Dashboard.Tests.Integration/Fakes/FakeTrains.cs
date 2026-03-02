using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

#pragma warning disable CS8766 // Nullability mismatch on Metadata property inherited from EffectTrain

namespace Trax.Dashboard.Tests.Integration.Fakes;

// --- Simple train A ---
public record FakeInputA;

public interface IFakeTrainA : IServiceTrain<FakeInputA, string> { }

public class FakeTrainA : ServiceTrain<FakeInputA, string>, IFakeTrainA
{
    protected override Task<Either<Exception, string>> RunInternal(FakeInputA input) =>
        Task.FromResult<Either<Exception, string>>("ok");
}

// --- Simple train B ---
public record FakeInputB;

public interface IFakeTrainB : IServiceTrain<FakeInputB, int> { }

public class FakeTrainB : ServiceTrain<FakeInputB, int>, IFakeTrainB
{
    protected override Task<Either<Exception, int>> RunInternal(FakeInputB input) =>
        Task.FromResult<Either<Exception, int>>(0);
}

// --- Simple train C ---
public record FakeInputC;

public interface IFakeTrainC : IServiceTrain<FakeInputC, bool> { }

public class FakeTrainC : ServiceTrain<FakeInputC, bool>, IFakeTrainC
{
    protected override Task<Either<Exception, bool>> RunInternal(FakeInputC input) =>
        Task.FromResult<Either<Exception, bool>>(true);
}

// --- Train with generic input/output types for friendly name tests ---
public interface IFakeGenericTrain : IServiceTrain<List<string>, Dictionary<string, int>> { }

public class FakeGenericTrain
    : ServiceTrain<List<string>, Dictionary<string, int>>,
        IFakeGenericTrain
{
    protected override Task<Either<Exception, Dictionary<string, int>>> RunInternal(
        List<string> input
    ) => Task.FromResult<Either<Exception, Dictionary<string, int>>>(new Dictionary<string, int>());
}

// --- Non-train service for negative tests ---
public interface INotATrain
{
    string DoSomething();
}

public class NotATrain : INotATrain
{
    public string DoSomething() => "not a train";
}
