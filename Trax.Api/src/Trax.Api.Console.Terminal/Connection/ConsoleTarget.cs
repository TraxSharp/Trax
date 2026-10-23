using System.Net;
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
        // A user name or password in the URL is never printed back, nor sent.
        if (
            Uri.TryCreate(host, UriKind.Absolute, out var root)
                ? root.UserInfo.Length > 0
                : host.Contains('@')
        )
            throw new InvalidOperationException(
                "--host carries a user name or password. Give the URL alone, and the credential with "
                    + $"--api-key-file, --token-file, {ApiKeyVariable} or {TokenVariable}."
            );
        if (root is null || root.Scheme is not ("http" or "https"))
            throw new InvalidOperationException(
                $"--host '{TerminalText.Line(host)}' is not an http or https URL, such as https://ops.example.com."
            );
        // The endpoint must be on the host named: a path such as //other.example.com/x is another host's.
        if (
            !graphQLPath.StartsWith('/')
            || !Uri.TryCreate(root, graphQLPath, out var endpoint)
            || endpoint.UserInfo.Length > 0
            || Uri.Compare(
                endpoint,
                root,
                UriComponents.SchemeAndServer,
                UriFormat.UriEscaped,
                StringComparison.OrdinalIgnoreCase
            ) != 0
        )
            throw new InvalidOperationException(
                $"--graphql-path '{TerminalText.Line(graphQLPath)}' is not a path on the host, such as "
                    + $"{DefaultGraphQLPath}."
            );

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

    /// <summary>The largest credential file read; a key or a token is far smaller.</summary>
    public const int MaxFileSize = 64 * 1024;

    // Other users' access to the file is as good as the credential itself, so a file they can read or write is
    // refused rather than warned of. A device, a pipe or anything longer than a credential is refused too.
    private static string Read(FileInfo file, string option)
    {
        var path = file.FullName;
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            var length = stream.Length;
            if (length > MaxFileSize)
                throw new InvalidOperationException(
                    $"{option}: {path} is larger than {MaxFileSize / 1024} KiB, which no credential is."
                );
            var buffer = new byte[MaxFileSize + 1];
            var read = 0;
            int got;
            while (
                read < buffer.Length && (got = stream.Read(buffer, read, buffer.Length - read)) > 0
            )
                read += got;
            // A regular file holds as much as its length says; a device such as /dev/zero says 0 and holds more.
            if (read > length)
                throw new InvalidOperationException($"{option}: {path} is not a regular file.");
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(stream.SafeFileHandle);
                const UnixFileMode others =
                    UnixFileMode.GroupRead
                    | UnixFileMode.GroupWrite
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherWrite;
                if ((mode & others) != 0)
                    throw new InvalidOperationException(
                        $"{option}: other users can read or change {path}. Make it yours alone with "
                            + $"chmod 600 {path}."
                    );
            }
            text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{option}: cannot read {path} ({ex.Message}).",
                ex
            );
        }
        return Value(text) ?? throw new InvalidOperationException($"{option}: {path} is empty.");
    }

    // The first line, trimmed, so a file written with a trailing newline holds the same credential.
    private static string? Value(string? text) =>
        text?.Split('\n', 2)[0].Trim() is { Length: > 0 } line ? line : null;

    private static bool IsThisMachine(Uri endpoint) =>
        endpoint.IsLoopback
        || (IPAddress.TryParse(endpoint.Host, out var address) && IPAddress.IsLoopback(address));
}
