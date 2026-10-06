namespace Trax.Samples.Recovery.Auth;

/// <summary>
/// The demo API key the page sends. It is published in this repository, so the host registers it only
/// in Development, and a key carrying <c>do-not-use-in-production</c> refuses to start anywhere else.
/// </summary>
public static class DemoKeys
{
    /// <summary>Role <c>Operator</c>: the operations view, with every answer and step name.</summary>
    public const string Operator = "recovery-operator-key-do-not-use-in-production";

    /// <summary>
    /// Role <c>Viewer</c>: the broadcast view. It may follow a run's steps but sees no answers and no
    /// step names on a track, which is why the page uses the operator key.
    /// </summary>
    public const string Viewer = "recovery-viewer-key-do-not-use-in-production";
}
