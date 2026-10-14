// What the examples on mediator/limit-concurrent-train-runs.md take as given: the train they limit.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record CombatInput(string AttackerId, string DefenderId);

public record CombatResult(string WinnerId);

public interface IResolveCombatTrain : IServiceTrain<CombatInput, CombatResult>;

public class ResolveCombatJunction : Junction<CombatInput, CombatResult>
{
    public override Task<CombatResult> Run(CombatInput input) =>
        Task.FromResult(new CombatResult(input.AttackerId));
}
