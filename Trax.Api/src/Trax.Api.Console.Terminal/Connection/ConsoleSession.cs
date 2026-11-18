using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Connection;

/// <summary>
/// One connection to a host: the HTTP client and the socket the generated client sends through, carrying the
/// credential the operations gate checks.
/// </summary>
internal sealed partial class ConsoleSession : IAsyncDisposable
{
    private readonly HttpClient? _http;
    private readonly GraphQLSubscriptionSocket? _socket;
    private readonly OutdatedTransport _transport;

    private ConsoleSession(ConsoleTarget target, ITraxTransport transport)
    {
        Target = target;
        _transport = new OutdatedTransport(transport);
        Client = new TraxClient(_transport);
    }

    private ConsoleSession(
        ConsoleTarget target,
        HttpClient http,
        GraphQLSubscriptionSocket socket,
        bool testHost
    )
    {
        Target = target;
        _http = http;
        _socket = socket;
        _transport = new OutdatedTransport(
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = http,
                    Endpoint = target.Endpoint,
                    Socket = socket,
                    // ConsoleTarget refuses a credential over plain HTTP to another machine; without one there is
                    // nothing to keep from the network.
                    AllowInsecureTransport = target.Credential is null,
                    // A test host's handler answers in process and follows no redirect. The network's is a
                    // SocketsHttpHandler the transport checks itself.
                    HandlerFollowsNoRedirects = testHost,
                    ById = _ => !target.SendText,
                }
            )
        );
        Client = new TraxClient(_transport);
    }

    /// <summary>What it talks to.</summary>
    public ConsoleTarget Target { get; }

    /// <summary>The generated client's runtime; the documents are its extension methods.</summary>
    public TraxClient Client { get; }

    /// <summary>
    /// The version of trax-console, as a host reports its own: the package version without build metadata. Both are
    /// built from one release, and the documents this tool sends are that release's.
    /// </summary>
    public static string ToolVersion { get; } =
        (
            typeof(ConsoleSession)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "0.0.0"
        ).Split('+')[0];

    /// <summary>
    /// Opens a session. <paramref name="handler"/> replaces the network for a test host; the session owns neither it
    /// nor <paramref name="configureSocket"/>'s socket options.
    /// </summary>
    public static ConsoleSession Open(
        ConsoleTarget target,
        HttpMessageHandler? handler = null,
        Action<System.Net.WebSockets.ClientWebSocketOptions>? configureSocket = null
    )
    {
        // The credential is a default header, so a redirect would carry it to wherever the host points. None is
        // followed: a 3xx is a failure that names where it pointed.
        var http = new HttpClient(
            new NoRedirects
            {
                InnerHandler = handler ?? new SocketsHttpHandler { AllowAutoRedirect = false },
            },
            disposeHandler: handler is null
        );
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("trax-console", ToolVersion)
        );
        switch (target.Credential)
        {
            case Credential.ApiKey key:
                http.DefaultRequestHeaders.Add("X-Api-Key", key.Key);
                break;
            case Credential.Bearer bearer:
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    bearer.Token
                );
                break;
        }
        var socket = new GraphQLSubscriptionSocket(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = target.Socket,
                AllowInsecureTransport = target.Credential is null,
                ConnectionParams = _ =>
                    ValueTask.FromResult<JsonObject?>(
                        target.Credential switch
                        {
                            Credential.ApiKey key => new JsonObject { ["apiKey"] = key.Key },
                            Credential.Bearer bearer => new JsonObject
                            {
                                ["authToken"] = bearer.Token,
                            },
                            _ => new JsonObject(),
                        }
                    ),
                ConfigureSocket = configureSocket,
            }
        );
        return new ConsoleSession(target, http, socket, testHost: handler is not null);
    }

    /// <summary>A session over <paramref name="transport"/>, for a test that scripts the host.</summary>
    internal static ConsoleSession Over(ConsoleTarget target, ITraxTransport transport) =>
        new(target, transport);

    /// <summary>
    /// Asks the host which Trax release it runs and refuses one of another: the console's documents are content
    /// addressed, so a host of another release registers others and answers these <c>CLIENT_OUTDATED</c>.
    /// </summary>
    public async Task<VersionCheck> CheckVersionAsync(CancellationToken cancellationToken = default)
    {
        var answer = await Client.ServerVersionAsync(cancellationToken).ConfigureAwait(false);
        if (!answer.Succeeded)
            return new VersionCheck.Failed(
                answer.Code,
                answer.Message ?? "The host answered without its version."
            );
        var host = answer.Data!.Operations?.Config.Version;
        if (host is null)
            return new VersionCheck.Failed(
                Code: null,
                "The host answered no operations: the credential may not reach the operations surface."
            );
        // It is printed, and put in an install line the operator may paste into a shell.
        if (!IsRelease(host))
            return new VersionCheck.Failed(
                Code: null,
                "The host answered a version that is not a Trax release number."
            );
        _transport.HostVersion = host;
        return string.Equals(host, ToolVersion, StringComparison.Ordinal)
            ? new VersionCheck.Same(host)
            : new VersionCheck.Other(host);
    }

    /// <summary>The install line for the trax-console that matches a host.</summary>
    /// <exception cref="ArgumentException"><paramref name="version"/> is not a release number.</exception>
    public static string InstallLine(string version) =>
        IsRelease(version)
            ? $"dotnet tool update -g Trax.Api.Console.Terminal --version {version}"
            : throw new ArgumentException(
                "Not a release number, so not put in an install line.",
                nameof(version)
            );

    /// <summary>Whether <paramref name="version"/> is a release number: SemVer, without build metadata.</summary>
    public static bool IsRelease(string version) =>
        version.Length <= 64 && ReleaseNumber.IsMatch(version);

    [GeneratedRegex(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ReleaseNumber { get; }

    /// <summary>Closes the socket and the HTTP client.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_socket is not null)
            await _socket.DisposeAsync().ConfigureAwait(false);
        _http?.Dispose();
    }
}
