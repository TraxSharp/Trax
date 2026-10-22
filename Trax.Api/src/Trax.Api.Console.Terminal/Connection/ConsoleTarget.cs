using System.Net;

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

/// <summary>Where the console talks to and as whom.</summary>
/// <param name="Endpoint">The host's GraphQL endpoint over HTTP(S).</param>
/// <param name="Socket">The same endpoint over WS(S), for the operator streams.</param>
/// <param name="Credential">What the gate checks, or null for a host whose operations surface is anonymous.</param>
/// <param name="SendText">Send each document's text rather than its id, for a host that runs no persisted operations.</param>
internal sealed record ConsoleTarget(
    Uri Endpoint,
    Uri Socket,
    Credential? Credential,
    bool SendText
)
{
    public const string ApiKeyVariable = "TRAX_CONSOLE_API_KEY";
    public const string TokenVariable = "TRAX_CONSOLE_TOKEN";
    public const string HostVariable = "TRAX_CONSOLE_HOST";
    public const string DefaultGraphQLPath = "/trax/graphql";

    /// <summary>
    /// The target for <paramref name="host"/>. The credential comes from a file (<paramref name="apiKeyFile"/>,
    /// <paramref name="tokenFile"/>) or the environment, never from the command line, where it would land in shell
    /// history and in the process list. One credential at most. A credential is sent only over HTTPS, or to this
    /// machine.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host is not an http(s) URL, two credentials are given, a file
    /// cannot be read or is empty, or a credential would go over plain HTTP to another machine.</exception>
    public static ConsoleTarget Resolve(
        string? host,
        string graphQLPath,
        FileInfo? apiKeyFile,
        FileInfo? tokenFile,
        bool sendText,
        Func<string, string?> environment
    )
    {
        host ??= environment(HostVariable);
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException(
                $"Name the host with --host https://ops.example.com, or set {HostVariable}."
            );
        if (
            !Uri.TryCreate(host, UriKind.Absolute, out var root)
            || root.Scheme is not ("http" or "https")
        )
            throw new InvalidOperationException(
                $"--host '{host}' is not an http or https URL, such as https://ops.example.com."
            );
        if (!graphQLPath.StartsWith('/'))
            throw new InvalidOperationException(
                $"--graphql-path '{graphQLPath}' is not a path on the host, such as {DefaultGraphQLPath}."
            );

        var endpoint = new Uri(root, graphQLPath);
        var socket = new UriBuilder(endpoint)
        {
            Scheme = endpoint.Scheme == "https" ? "wss" : "ws",
        }.Uri;

        var credentials = new List<Credential>();
        if (apiKeyFile is not null)
            credentials.Add(new Credential.ApiKey(Read(apiKeyFile, "--api-key-file")));
        if (tokenFile is not null)
            credentials.Add(new Credential.Bearer(Read(tokenFile, "--token-file")));
        // A file named on the command line wins over the environment.
        if (credentials.Count == 0)
        {
            if (Value(environment(ApiKeyVariable)) is { } key)
                credentials.Add(new Credential.ApiKey(key));
            if (Value(environment(TokenVariable)) is { } token)
                credentials.Add(new Credential.Bearer(token));
        }
        if (credentials.Count > 1)
            throw new InvalidOperationException(
                $"Give one credential: an API key ({ApiKeyVariable} or --api-key-file) or a bearer token "
                    + $"({TokenVariable} or --token-file), not both."
            );
        var credential = credentials.SingleOrDefault();

        if (credential is not null && endpoint.Scheme == "http" && !IsThisMachine(endpoint))
            throw new InvalidOperationException(
                $"{endpoint} is plain HTTP to another machine, and the credential would cross the network in "
                    + "the clear. Use https."
            );
        return new ConsoleTarget(endpoint, socket, credential, sendText);
    }

    private static string Read(FileInfo file, string option)
    {
        string text;
        try
        {
            text = File.ReadAllText(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{option}: cannot read {file.FullName} ({ex.Message}).",
                ex
            );
        }
        return Value(text)
            ?? throw new InvalidOperationException($"{option}: {file.FullName} is empty.");
    }

    // The first line, trimmed, so a file written with a trailing newline holds the same credential.
    private static string? Value(string? text) =>
        text?.Split('\n', 2)[0].Trim() is { Length: > 0 } line ? line : null;

    private static bool IsThisMachine(Uri endpoint) =>
        endpoint.IsLoopback
        || (IPAddress.TryParse(endpoint.Host, out var address) && IPAddress.IsLoopback(address));
}
