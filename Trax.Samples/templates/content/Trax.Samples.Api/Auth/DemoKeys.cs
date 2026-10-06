namespace Trax.Samples.Api.Auth;

/// <summary>
/// Plaintext demonstration API key. NO WARRANTY: this is not a secret. The
/// <c>-do-not-use-in-production</c> marker makes that explicit. Replace this with real credential
/// handling (a resolver backed by your user store) before shipping.
/// </summary>
public static class DemoKeys
{
    public const string DemoKey = "demo-key-do-not-use-in-production";
}
