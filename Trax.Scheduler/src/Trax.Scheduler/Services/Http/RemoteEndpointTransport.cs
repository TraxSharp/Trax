namespace Trax.Scheduler.Services.Http;

/// <summary>
/// What the scheduler checks of a runner's base URL before it sends anything to it. A runner's
/// status codes and error bodies are not signed, only a run's success body is, so an answer that
/// crosses the network in cleartext could be changed on the way. Plain <c>http://</c> is accepted
/// only to this machine, or when the host says so with <c>AllowInsecureTransport</c>.
/// </summary>
internal static class RemoteEndpointTransport
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when <paramref name="baseUrl"/> is plain
    /// <c>http://</c> to a host that is not loopback and <paramref name="allowInsecureTransport"/>
    /// is off.
    /// </summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <param name="allowInsecureTransport">Whether the host opted in to cleartext.</param>
    /// <param name="optionsName">The options type, for the message.</param>
    public static void Ensure(string? baseUrl, bool allowInsecureTransport, string optionsName)
    {
        if (allowInsecureTransport || !IsCleartextToAnotherHost(baseUrl, out var authority))
            return;

        throw new InvalidOperationException(
            $"The runner {authority} is not reached over TLS. A runner's status codes and error "
                + "bodies are not signed, so an answer in cleartext could be changed on the way, and "
                + "whatever authenticates the requests would cross the network unencrypted. Use an "
                + $"https:// BaseUrl, a loopback host, or set {optionsName}.AllowInsecureTransport "
                + "when the network to the runner is trusted."
        );
    }

    // Plain http:// to a host that is not this machine (localhost, 127.0.0.0/8, ::1). A URL that
    // does not parse is left to the HTTP client, which refuses it.
    private static bool IsCleartextToAnotherHost(string? baseUrl, out string authority)
    {
        authority = "";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;
        authority = $"{uri.Scheme}://{uri.Authority}";
        return !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !uri.IsLoopback;
    }
}
