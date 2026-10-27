namespace Trax.Client;

// What the client checks of an endpoint before it sends anything to it: a token in a header or in connection_init
// travels in cleartext over http:// or ws://, so neither is accepted unless the host is this machine or the caller says
// so.
internal static class TransportSecurity
{
    public static void RequireSecure(
        Uri endpoint,
        bool allowInsecure,
        string secureScheme,
        string paramName
    )
    {
        if (allowInsecure || IsSecure(endpoint, secureScheme))
            return;
        throw new ArgumentException(
            $"The endpoint {endpoint.Scheme}://{endpoint.Authority} is not over TLS, so whatever authenticates the "
                + $"client would travel in cleartext. Use {secureScheme}://, a loopback host, or set "
                + "AllowInsecureTransport when the network is trusted.",
            paramName
        );
    }

    // Over TLS, or to this machine (localhost, 127.0.0.0/8, ::1).
    internal static bool IsSecure(Uri endpoint, string secureScheme) =>
        endpoint.IsAbsoluteUri
        && (
            string.Equals(endpoint.Scheme, secureScheme, StringComparison.OrdinalIgnoreCase)
            || endpoint.IsLoopback
        );
}
