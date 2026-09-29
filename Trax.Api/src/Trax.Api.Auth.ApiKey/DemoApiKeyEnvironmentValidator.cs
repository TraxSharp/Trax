using Microsoft.Extensions.Hosting;

namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// Refuses to start a host outside Development when a key registered through
/// <see cref="ApiKeyBuilder.Add(string, string, string[])"/> carries
/// <see cref="ApiKeyBuilder.DemoKeyMarker"/>, the marker the Trax templates and samples put on
/// their plaintext demo keys. Registered only when such a key was added.
/// </summary>
internal sealed class DemoApiKeyEnvironmentValidator(IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                $"AddTraxApiKeyAuth() registered a key containing '{ApiKeyBuilder.DemoKeyMarker}', "
                    + $"which marks a published demo key, and the environment is "
                    + $"'{environment.EnvironmentName}'. Such keys start only in Development. "
                    + "Register real keys (keys.AddHashed(...) from a secret store, or a resolver "
                    + "via AddTraxApiKeyAuth<TResolver>()) outside Development."
            );
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
