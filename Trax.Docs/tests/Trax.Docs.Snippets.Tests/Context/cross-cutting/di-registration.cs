// What the compiled example on cross-cutting/di-registration.md takes as given.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record CreateOrderInput(string Sku);

public interface ICreateOrderTrain : IServiceTrain<CreateOrderInput, Unit>;

public class CreateOrderTrain : ServiceTrain<CreateOrderInput, Unit>, ICreateOrderTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        IChain<IValidateOrderJunction>().Resolve();
}

public interface IValidateOrderJunction : IJunction<CreateOrderInput, Unit>;

public class ValidateOrderJunction : Junction<CreateOrderInput, Unit>, IValidateOrderJunction
{
    public override Task<Unit> Run(CreateOrderInput input) => Task.FromResult(Unit.Default);
}
