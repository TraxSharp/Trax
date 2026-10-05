using Microsoft.AspNetCore.Hosting;

namespace Trax.Samples.StateMachine.E2E.Factories;

/// <summary>
/// The StateMachine sample started in Production, with an optional extra configuration step. Started
/// this way it registers no demo key, so no API key authenticates anyone.
/// </summary>
public class ProductionStateMachineApiFactory(Action<IWebHostBuilder>? configure = null)
    : StateMachineApiFactory
{
    protected override string Environment => "Production";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        configure?.Invoke(builder);
    }
}
