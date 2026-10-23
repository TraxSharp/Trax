using System.Text;
using Microsoft.Extensions.Configuration;

namespace Trax.Samples.ContentShield;

/// <summary>
/// The key every process on the <see cref="LifecycleEvents.ExchangeName"/> exchange signs its
/// train events with. A receiver drops any event not signed with it, so a process that can reach
/// the broker but does not hold the key cannot put events in front of this sample's subscribers.
/// </summary>
public static class BroadcastSigningKey
{
    /// <summary>Configuration key holding the base64 of 32 or more random bytes.</summary>
    public const string ConfigurationKey = "Trax:BroadcastSigningKey";

    /// <summary>
    /// Published in this repository, so not a secret: used only in Development, when no key is
    /// configured.
    /// </summary>
    private const string DemoKey = "contentshield-broadcast-signing-key-do-not-use-in-production";

    /// <summary>
    /// The configured key, or in Development the demo key. Outside Development a missing key
    /// stops the host: its events would be refused by every other process on the exchange.
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
                + "(openssl rand -base64 32), the same value in every process on the exchange."
        );
    }
}
