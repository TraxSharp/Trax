namespace Trax.Samples.Scheduling.Host;

/// <summary>
/// The sample's demonstration API key. NO WARRANTY: it is printed in the source, so it is not a
/// secret, and the <c>do-not-use-in-production</c> marker makes Trax refuse to start a host that
/// registers it outside Development. Real keys come from a secret store.
/// </summary>
public static class DemoKeys
{
    /// <summary>The role <c>GateOperations</c> requires on the <c>operations</c> namespace.</summary>
    public const string OperatorRole = "Operator";

    /// <summary>Send as <c>X-Api-Key</c>. Holds <see cref="OperatorRole"/>.</summary>
    public const string OperatorKey = "operator-key-do-not-use-in-production";
}
