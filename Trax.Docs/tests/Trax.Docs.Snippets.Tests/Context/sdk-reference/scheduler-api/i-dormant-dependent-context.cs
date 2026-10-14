// What the examples on sdk-reference/scheduler-api/i-dormant-dependent-context.md take as given:
// the parent's input and the dormant child train it activates.
global using Trax.Core.Junction;
global using Trax.Scheduler.Services.DormantDependentContext;

public record ChildInput : IManifestProperties
{
    public string Data { get; init; } = "";
}

public interface IChildTrain : IServiceTrain<ChildInput, Unit>;

public record DispatchItem(string Id, string Data);

public record DispatchInput(string RuntimeData, IReadOnlyList<DispatchItem> Items);
