namespace Trax.Samples.PersistedOperations.Api.Auth;

/// <summary>
/// Plaintext demonstration API key for the persisted-operations management mutations. NO
/// WARRANTY: it is published in this repository and is not a secret. Program.cs registers it only
/// in Development, and the <c>do-not-use-in-production</c> marker makes Trax.Api refuse to start
/// with it anywhere else.
/// </summary>
public static class DemoKeys
{
    /// <summary>Holds the <see cref="OperatorRole"/> role.</summary>
    public const string OperatorKey = "operator-key-do-not-use-in-production";

    /// <summary>The role the <c>operations</c> namespace requires.</summary>
    public const string OperatorRole = "Operator";
}
