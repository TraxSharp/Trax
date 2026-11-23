using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Trax.Api.Console.Terminal.Lists;

namespace Trax.Api.Console.Terminal.Connection;

/// <summary>A credential the host's operations gate checks: an API key or a bearer token.</summary>
internal abstract record Credential
{
    private Credential() { }

    /// <summary>Sent as <c>X-Api-Key</c>, and as <c>apiKey</c> in the socket's <c>connection_init</c>.</summary>
    public sealed record ApiKey(string Key) : Credential;

    /// <summary>Sent as <c>Authorization: Bearer</c>, and as <c>authToken</c> in the socket's <c>connection_init</c>.</summary>
    public sealed record Bearer(string Token) : Credential;
}
