using Microsoft.Extensions.Hosting;

namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// Refuses to start a host outside Development when a key registered through
/// <see cref="ApiKeyBuilder.Add(string, string, string[])"/> is shorter than
/// <see cref="ApiKeyBuilder.MinimumKeyLength"/>. A key is the whole credential, so its length is
/// what stands between a caller and guessing it. Registered only when such a key was added, the
/// way <see cref="DemoApiKeyEnvironmentValidator"/> is.
/// </summary>
internal sealed class ShortApiKeyEnvironmentValidator(IHostEnvironment environment) : StartupGate
{
    protected override Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                "AddTraxApiKeyAuth() registered a key shorter than "
                    + $"{ApiKeyBuilder.MinimumKeyLength} characters, and the environment is "
                    + $"'{environment.EnvironmentName}'. Such keys start only in Development. "
                    + $"Use keys of at least {ApiKeyBuilder.MinimumKeyLength} random characters "
                    + "(for example 32 bytes from a cryptographic generator, base64-encoded), "
                    + "registered with keys.AddHashed(...) from a secret store or through a "
                    + "resolver via AddTraxApiKeyAuth<TResolver>()."
            );
        return Task.CompletedTask;
    }
}
