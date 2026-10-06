using System.Text;
using Microsoft.Extensions.Configuration;

namespace Trax.Samples.ContentShield;

/// <summary>
/// The key the API signs each job with and the runner verifies before it runs anything. A runner
/// trusts what it is sent (the API already authorized the caller), so this key is what stops anyone
/// else from posting work to <c>/trax/execute</c> or <c>/trax/run</c>.
/// </summary>
public static class RunnerSigningKey
{
    /// <summary>Configuration key holding the base64 of 32 or more random bytes.</summary>
    public const string ConfigurationKey = "Trax:RunnerSigningKey";

    /// <summary>
    /// Published in this repository, so not a secret: used only in Development, when no key is
    /// configured.
    /// </summary>
    private const string DemoKey = "contentshield-runner-signing-key-do-not-use-in-production";

    /// <summary>
    /// The configured key, or in Development the demo key. Outside Development a missing key
    /// stops the host: the API cannot sign, and the runner would refuse every request.
    /// </summary>
    public static byte[] Resolve(IConfiguration configuration, bool isDevelopment)
    {
        var configured = configuration[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(configured))
            return Convert.FromBase64String(configured);

        if (isDevelopment)
            return Encoding.UTF8.GetBytes(DemoKey);

        throw new InvalidOperationException(
            $"Set {ConfigurationKey} to the base64 of 32 or more random bytes "
                + "(openssl rand -base64 32), the same value on the API and the runner."
        );
    }
}
