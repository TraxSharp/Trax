using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>
/// Where a host that signs people in by their tailnet identity (<c>AddTraxTailscaleAuth</c>) may be deployed: only as
/// the hub of a kubernetes environment with <c>"exposure": "tailnet"</c>, where trax renders the proxy that proves
/// itself to the host and nothing else reaches it. Anywhere else the identity headers could come from anyone who
/// reaches the host, so the deploy is refused before anything is built.
/// </summary>
internal static class TailnetSignIn
{
    /// <summary>The handler <c>AddTraxTailscaleAuth</c> registers, for a host that renamed its scheme.</summary>
    public const string Handler = "TailscaleAuthHandler";

    /// <summary>Whether <paramref name="export"/> lists the tailnet sign-in scheme.</summary>
    public static bool Listed(DeployManifest export) =>
        export.Authentication.Any(a =>
            string.Equals(a.Name, DeployExposure.TailscaleScheme, StringComparison.Ordinal)
            || string.Equals(a.Handler, Handler, StringComparison.Ordinal)
        );

    /// <summary>
    /// Refuses every service whose host lists the scheme, except <paramref name="allowed"/>, saying
    /// <paramref name="why"/>.
    /// </summary>
    public static void Refuse(
        IReadOnlyDictionary<string, DeployManifest> exports,
        string why,
        string? allowed = null
    )
    {
        foreach (var (name, export) in exports.OrderBy(e => e.Key, StringComparer.Ordinal))
            if (Listed(export) && !string.Equals(name, allowed, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Service '{name}' signs people in by their tailnet identity (AddTraxTailscaleAuth), and {why} "
                        + "Those headers are trusted only on the hub of a kubernetes environment with \"exposure\": "
                        + "\"tailnet\", behind the proxy trax renders. Nothing was built or applied."
                );
    }
}
