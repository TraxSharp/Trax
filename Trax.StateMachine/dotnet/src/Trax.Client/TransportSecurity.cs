using System.Runtime.CompilerServices;

namespace Trax.Client;

// What the client checks of an endpoint before it sends anything to it: a token in a header or in connection_init
// travels in cleartext over http:// or ws://, so neither is accepted unless the host is this machine or the caller says
// so.
internal static class TransportSecurity
{
    // The endpoint is the one requests go to, resolved against the client's base address when it has one. One that is
    // still relative has nowhere to go, whether or not cleartext is allowed.
    public static void RequireSecure(
        Uri endpoint,
        bool allowInsecure,
        string secureScheme,
        string paramName
    )
    {
        if (!endpoint.IsAbsoluteUri)
            throw new ArgumentException(
                $"The endpoint '{endpoint}' is relative, and there is no base address to resolve it against.",
                paramName
            );
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

    // A client is accepted only when the check can show its handler follows no redirect: an HttpClientHandler or a
    // SocketsHttpHandler with AllowAutoRedirect off at the end of its chain. Any other handler is accepted only when the
    // caller vouches for it; a known handler that follows redirects is refused whether or not they do. The delegating
    // handlers before it are required, as the options document, to pass each request on once and send none of their
    // own; what one does inside SendAsync is not something a check of the chain can read.
    public static void RequireNoRedirects(HttpClient? http, bool vouched, string paramName)
    {
        if (http is null)
            return;
        switch (FollowsRedirects(http))
        {
            case false:
                return;
            case null when vouched:
                return;
            case true:
                throw new ArgumentException(
                    "The HttpClient must not follow redirects. Build it on a handler with automatic redirects "
                        + "off: new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }).",
                    paramName
                );
            case null:
                throw new ArgumentException(
                    "The HttpClient must not follow redirects, and its handler is not one the client can check. "
                        + "Build it on a SocketsHttpHandler or HttpClientHandler with AllowAutoRedirect = false "
                        + "(behind any delegating handlers), or, for a handler that follows no redirects, set "
                        + "HandlerFollowsNoRedirects.",
                    paramName
                );
        }
    }

    // Whether the client's handler follows redirects: true or false for an HttpClientHandler or SocketsHttpHandler at the
    // end of its chain of delegating handlers, and null when the check cannot tell (a handler of another kind, a chain
    // with no handler at its end, or a runtime whose HttpClient keeps its handler elsewhere). A redirect is followed
    // inside the handler, before any answer comes back to be checked, so it has to be known before the first request.
    // The handler is not public; it is read through an accessor.
    internal static bool? FollowsRedirects(HttpClient http)
    {
        HttpMessageHandler? handler;
        try
        {
            handler = HandlerOf(http);
        }
        catch (MissingFieldException)
        {
            return null;
        }
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;
        if (handler is HttpClientHandler client)
            return client.AllowAutoRedirect;
        if (!OperatingSystem.IsBrowser() && handler is SocketsHttpHandler sockets)
            return sockets.AllowAutoRedirect;
        return null;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_handler")]
    private static extern ref HttpMessageHandler HandlerOf(HttpMessageInvoker invoker);
}
