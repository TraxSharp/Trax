using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ApiAudit.Trains;

public record EchoInput(string Message);

public record EchoOutput(string Echoed);

public interface IEchoTrain : IServiceTrain<EchoInput, EchoOutput>;

[TraxQuery]
public class EchoTrain : ServiceTrain<EchoInput, EchoOutput>, IEchoTrain
{
    protected override EchoOutput Junctions() => Chain<EchoJunction>();
}

public class EchoJunction : Junction<EchoInput, EchoOutput>
{
    public override Task<EchoOutput> Run(EchoInput input) =>
        Task.FromResult(new EchoOutput(input.Message));
}
