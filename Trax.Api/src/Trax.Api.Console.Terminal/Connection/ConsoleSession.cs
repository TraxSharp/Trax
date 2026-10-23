using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Connection;

/// <summary>
/// One connection to a host: the HTTP client and the socket the generated client sends through, carrying the
/// credential the operations gate checks.
/// </summary>
internal sealed class ConsoleSession : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly GraphQLSubscriptionSocket _socket;

    private ConsoleSession(ConsoleTarget target, HttpClient http, GraphQLSubscriptionSocket socket)
    {
        Target = target;
        _http = http;
        _socket = socket;
        Client = new TraxClient(
            new TraxHttpTransport(
                new TraxHttpTransportOptions
                {
                    Http = http,
                    Endpoint = target.Endpoint,
                    Socket = socket,
                    // ConsoleTarget refuses a credential over plain HTTP to another machine; without one there is
                    // nothing to keep from the network.
                    AllowInsecureTransport = target.Credential is null,
                    ById = _ => !target.SendText,
                }
            )
        );
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
        return new ConsoleSession(target, http, socket);
    }

    /// <summary>
    /// Asks the host which Trax release it runs and refuses one of another: the console's documents are content
    /// addressed, so a host of another release registers others and answers these <c>CLIENT_OUTDATED</c>.
    /// </summary>
    public async Task<VersionCheck> CheckVersionAsync(CancellationToken cancellationToken = default)
    {
        var answer = await Client.ServerVersionAsync(cancellationToken);
        if (!answer.Succeeded)
            return new VersionCheck.Failed(
                answer.Code,
                answer.Message ?? "The host answered without its version."
            );
        var host = answer.Data!.Operations?.Config.Version;
        if (host is null)
            return new VersionCheck.Failed(
                null,
                "The host answered no operations: the credential may not reach the operations surface."
            );
        return host == ToolVersion ? new VersionCheck.Same(host) : new VersionCheck.Other(host);
    }

    /// <summary>The install line for the trax-console that matches a host.</summary>
    public static string InstallLine(string version) =>
        $"dotnet tool update -g Trax.Api.Console.Terminal --version {version}";

    /// <summary>Closes the socket and the HTTP client.</summary>
    public async ValueTask DisposeAsync()
    {
        await _socket.DisposeAsync();
        _http.Dispose();
    }
}

/// <summary>Turns a redirect into a failure, so the credential goes to the endpoint the operator named and no other.</summary>
internal sealed class NoRedirects : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await base.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode is < 300 or >= 400)
            return response;
        var location = response.Headers.Location;
        response.Dispose();
        throw new HttpRequestException(
            $"{request.RequestUri} redirected to {Shown(request.RequestUri, location)}, and trax-console follows no "
                + "redirect. Give --host and --graphql-path of the endpoint itself."
        );
    }

    // Where it pointed, without userinfo, on one line, and short.
    private static string Shown(Uri? from, Uri? location)
    {
        if (location is null)
            return "nowhere";
        if (!location.IsAbsoluteUri && from is not null)
            location = new Uri(from, location);
        var text = location.IsAbsoluteUri
            ? new UriBuilder(location) { UserName = "", Password = "" }.Uri.ToString()
            : location.OriginalString;
        text = TerminalText.Line(text);
        return text.Length > 200 ? text[..200] + "..." : text;
    }
}

/// <summary>What <see cref="ConsoleSession.CheckVersionAsync"/> found.</summary>
internal abstract record VersionCheck
{
    private VersionCheck() { }

    /// <summary>The host runs this tool's release.</summary>
    public sealed record Same(string Version) : VersionCheck;

    /// <summary>The host runs another release.</summary>
    public sealed record Other(string HostVersion) : VersionCheck
    {
        public string Message =>
            $"This host runs Trax .NET {HostVersion}; trax-console is {ConsoleSession.ToolVersion}. The console sends "
            + $"the documents of its own release, which this host does not serve. Install the matching one: "
            + ConsoleSession.InstallLine(HostVersion);
    }

    /// <summary>The host did not say: refused, unreachable, or not serving operations.</summary>
    public sealed record Failed(string? Code, string Message) : VersionCheck;
}
