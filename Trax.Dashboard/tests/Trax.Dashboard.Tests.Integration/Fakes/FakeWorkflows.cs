using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

#pragma warning disable CS8766 // Nullability mismatch on Metadata property inherited from EffectWorkflow

namespace Trax.Dashboard.Tests.Integration.Fakes;

// --- Simple workflow A ---
public record FakeInputA;

public interface IFakeWorkflowA : IServiceTrain<FakeInputA, string> { }

public class FakeWorkflowA : ServiceTrain<FakeInputA, string>, IFakeWorkflowA
{
    protected override Task<Either<Exception, string>> RunInternal(FakeInputA input) =>
        Task.FromResult<Either<Exception, string>>("ok");
}

// --- Simple workflow B ---
public record FakeInputB;

public interface IFakeWorkflowB : IServiceTrain<FakeInputB, int> { }

public class FakeWorkflowB : ServiceTrain<FakeInputB, int>, IFakeWorkflowB
{
    protected override Task<Either<Exception, int>> RunInternal(FakeInputB input) =>
        Task.FromResult<Either<Exception, int>>(0);
}

// --- Simple workflow C ---
public record FakeInputC;

public interface IFakeWorkflowC : IServiceTrain<FakeInputC, bool> { }

public class FakeWorkflowC : ServiceTrain<FakeInputC, bool>, IFakeWorkflowC
{
    protected override Task<Either<Exception, bool>> RunInternal(FakeInputC input) =>
        Task.FromResult<Either<Exception, bool>>(true);
}

// --- Workflow with generic input/output types for friendly name tests ---
public interface IFakeGenericWorkflow : IServiceTrain<List<string>, Dictionary<string, int>> { }

public class FakeGenericWorkflow
    : ServiceTrain<List<string>, Dictionary<string, int>>,
        IFakeGenericWorkflow
{
    protected override Task<Either<Exception, Dictionary<string, int>>> RunInternal(
        List<string> input
    ) => Task.FromResult<Either<Exception, Dictionary<string, int>>>(new Dictionary<string, int>());
}

// --- Non-workflow service for negative tests ---
public interface INotAWorkflow
{
    string DoSomething();
}

public class NotAWorkflow : INotAWorkflow
{
    public string DoSomething() => "not a workflow";
}
