namespace Trax.Samples.Scheduler.Auth;

/// <summary>
/// Plaintext demonstration API key. NO WARRANTY: this is not a secret. The
/// <c>-do-not-use-in-production</c> marker makes that explicit. Replace this with real credential
/// handling (a resolver backed by your user store) before shipping.
/// </summary>
public static class DemoKeys
{
    /// <summary>Holds <see cref="OperatorRole"/>, which the operations namespace and the console need.</summary>
    public const string OperatorKey = "operator-key-do-not-use-in-production";

    public const string OperatorRole = "Operator";
}
