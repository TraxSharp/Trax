using Microsoft.AspNetCore.Hosting;

namespace Trax.Samples.Auth.E2E.Factories;

/// <summary>
/// The Auth sample started in Production, with whatever configuration a test supplies. With none,
/// it registers no authentication scheme at all.
/// </summary>
public class ProductionAuthApiFactory(
    IReadOnlyDictionary<string, string>? settings = null,
    Action<IWebHostBuilder>? configure = null
) : AuthApiFactory
{
    protected override string Environment => "Production";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            builder.UseSetting(key, value);
        configure?.Invoke(builder);
    }
}
