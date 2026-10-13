namespace Trax.Samples.Recovery.Browser.Fixtures;

/// <summary>
/// How long a browser test waits on each kind of signal. Every wait is on a locator, a request or the
/// database, never a delay (Trax.Docs/adr/0006), and each is bounded here (Trax.Docs/adr/0014).
/// </summary>
public static class BrowserTimeouts
{
    /// <summary>The page renders a state the server already holds.</summary>
    public static readonly TimeSpan Render = TimeSpan.FromSeconds(15);

    /// <summary>A build runs to its outcome, or the host's reconciler applies a timer or a recovery.</summary>
    public static readonly TimeSpan Server = TimeSpan.FromSeconds(60);

    public static float Ms(this TimeSpan timeout) => (float)timeout.TotalMilliseconds;
}
