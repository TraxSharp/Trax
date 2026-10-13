// What the example on core/move-work-into-the-chain.md names but does not declare.
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record ChildRequest(string Data);

public record ChildResult(string Data);

public record ParentInput(string ParentData, string ChildData);

public record ParentResult(string ParentData, ChildResult Child);

public interface IParentTrain : IServiceTrain<ParentInput, ParentResult>;

public class ValidateParentResult : Junction<ParentResult, ParentResult>
{
    public override Task<ParentResult> Run(ParentResult input) => Task.FromResult(input);
}
