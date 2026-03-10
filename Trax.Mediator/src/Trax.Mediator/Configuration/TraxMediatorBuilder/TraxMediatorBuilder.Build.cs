namespace Trax.Mediator.Configuration;

public partial class TraxMediatorBuilder
{
    internal MediatorConfiguration Build()
    {
        if (_assemblies.Count == 0)
        {
            throw new InvalidOperationException(
                "AddMediator() requires at least one assembly to scan for "
                    + "IServiceTrain<TIn, TOut> implementations. "
                    + "Call ScanAssemblies() with the assemblies containing your trains:\n\n"
                    + "  services.AddTrax(trax => trax\n"
                    + "      .AddEffects(effects => effects)\n"
                    + "      .AddMediator(mediator => mediator\n"
                    + "          .ScanAssemblies(typeof(Program).Assembly)\n"
                    + "      )\n"
                    + "  );\n"
            );
        }

        return new MediatorConfiguration
        {
            TrainLifetime = _lifetime,
            Assemblies = [.. _assemblies],
        };
    }
}
