namespace Trax.Samples.ApiAudit.Auth;

/// <summary>
/// Demo API keys used by the ApiAudit sample. For demonstration only: these
/// are plaintext constants published in the Trax repository. Any production
/// system that ships them is broken. See
/// <c>Trax.Api/SECURITY-DISCLAIMER.md</c>.
/// </summary>
public static class SampleKeys
{
    /// <summary>Resolves to user <c>alice</c> (display name <c>Alice</c>).</summary>
    public const string AliceKey = "alice-key";

    /// <summary>Resolves to user <c>bob</c> (display name <c>Bob</c>).</summary>
    public const string BobKey = "bob-key";
}
