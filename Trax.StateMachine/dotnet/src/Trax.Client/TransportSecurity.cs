using System.Runtime.CompilerServices;

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

    public static void RequireNoRedirects(HttpClient? http, string paramName)
    {
        if (http is null || !FollowsRedirects(http))
            return;
        throw new ArgumentException(
            "The HttpClient follows redirects, so a redirect would carry the request, its body and every default "
                + "header but Authorization, to whatever host it names. Build it on a handler with automatic "
                + "redirects off: new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }).",
            paramName
        );
    }

    // Whether the client's handler follows redirects. A redirect is followed inside the handler, before any answer
    // comes back to be checked, so it has to be known before the first request. The handler is not public; it is read
    // through an accessor, and a handler of a kind this does not know (a platform's own, a test's) is taken to follow
    // none: the transport still fails an answer from another host.
    internal static bool FollowsRedirects(HttpClient http)
    {
        HttpMessageHandler? handler;
        try
        {
            handler = HandlerOf(http);
        }
        catch (MissingFieldException)
        {
            return false;
        }
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;
        if (handler is HttpClientHandler client)
            return client.AllowAutoRedirect;
        if (!OperatingSystem.IsBrowser() && handler is SocketsHttpHandler sockets)
            return sockets.AllowAutoRedirect;
        return false;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_handler")]
    private static extern ref HttpMessageHandler HandlerOf(HttpMessageInvoker invoker);
}
