namespace Trax.Samples.EnergyHub.Hub;

/// <summary>
/// Plaintext demonstration API key for the hub's mutations and its <c>operations</c> namespace.
/// NO WARRANTY: it is published in this repository and is not a secret. Program.cs registers it
/// only in Development, and the <c>do-not-use-in-production</c> marker makes Trax.Api refuse to
/// start with it anywhere else.
/// </summary>
public static class DemoKeys
{
    /// <summary>Holds the <see cref="EnergyHubRoles.Operator"/> role.</summary>
    public const string OperatorKey = "energyhub-operator-key-do-not-use-in-production";
}
