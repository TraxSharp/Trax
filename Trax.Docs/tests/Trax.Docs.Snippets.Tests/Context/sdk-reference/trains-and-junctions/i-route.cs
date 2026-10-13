// The train the examples on sdk-reference/trains-and-junctions/i-route.md register and forward.
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public interface ICreateOrderTrain : IServiceTrain<CreateOrderInput, OrderResult>;

public class CreateOrderTrain : ServiceTrain<CreateOrderInput, OrderResult>, ICreateOrderTrain
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Task.FromResult(Resolve());
}
