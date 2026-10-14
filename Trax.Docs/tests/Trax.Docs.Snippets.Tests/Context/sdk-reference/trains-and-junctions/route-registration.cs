// The trains and the junction the example on sdk-reference/trains-and-junctions/route-registration.md
// registers.
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public interface ICreateOrderTrain : IServiceTrain<CreateOrderInput, OrderResult>;

public class CreateOrderTrain : ServiceTrain<CreateOrderInput, OrderResult>, ICreateOrderTrain
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Task.FromResult(Resolve());
}

public interface IReconcileTrain : IServiceTrain<Unit, Unit>;

public class ReconcileTrain : ServiceTrain<Unit, Unit>, IReconcileTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
}

public record Address(string Line1, string City);

public interface IFormatAddress : IJunction<Address, string>;

public class FormatAddress : Junction<Address, string>, IFormatAddress
{
    public override Task<string> Run(Address input) =>
        Task.FromResult($"{input.Line1}, {input.City}");
}
