using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>A document and the variables it is sent with.</summary>
/// <param name="Document">The document.</param>
/// <param name="Variables">Its variables as JSON; a document with none sends an empty object.</param>
public sealed record TraxRequest(TraxDocument Document, JsonObject Variables);

/// <summary>
/// How a request reaches a host. <see cref="TraxClient"/> never talks to the network itself: it is given a
/// <see cref="TraxHttpTransport"/>, or a transport of the app's own (a recording, a fake, a wrapper that counts what is
/// in flight).
/// </summary>
public interface ITraxTransport
{
    /// <summary>
    /// Sends a query or mutation. Never throws for the host's sake: a refusal is an answer with errors, and a host that
    /// could not be reached is an answer with a <see cref="TraxAnswer.Failure"/>. Throws only when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Follows a subscription, each frame a GraphQL result, until the returned handle is disposed.</summary>
    IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink);
}

/// <summary>How a <see cref="TraxHttpTransport"/> reaches its host.</summary>
public sealed record TraxHttpTransportOptions
{
    /// <summary>
    /// The client the requests go through. Set its authentication on it, a default header (<c>X-Api-Key</c>,
    /// <c>Authorization</c>) or a delegating handler, as for any other <see cref="System.Net.Http.HttpClient"/>.
    /// </summary>
    public required HttpClient Http { get; init; }

    /// <summary>
    /// The host's GraphQL endpoint (<c>https://host/trax/graphql</c>), or one relative to <see cref="Http"/>'s base
    /// address. It must be <c>https</c> unless its host is loopback or <see cref="AllowInsecureTransport"/> is set.
    /// </summary>
    public required Uri Endpoint { get; init; }

    /// <summary>
    /// Accepts an <c>http</c> endpoint on a host that is not loopback. Off by default: whatever authenticates the
    /// requests would travel in cleartext.
    /// </summary>
    public bool AllowInsecureTransport { get; init; }

    /// <summary>
    /// The largest response body the host may answer with, in bytes; 16 MiB by default. A larger one is a
    /// <see cref="TraxAnswer.Failure"/>, never read into memory.
    /// </summary>
    public long MaxResponseSize { get; init; } = DefaultMaxResponseSize;

    /// <summary>The response body limit when none is set: 16 MiB.</summary>
    public const long DefaultMaxResponseSize = 16 * 1024 * 1024;

    /// <summary>
    /// The socket subscriptions go over, usually a <see cref="GraphQLSubscriptionSocket"/>. Null fails every
    /// subscription at once.
    /// </summary>
    public ISubscriptionSocket? Socket { get; init; }

    /// <summary>
    /// Whether a document goes by its id rather than its text. By default every document that has an id does; a host
    /// that registered only some of them (or none, when it runs no persisted operations) names which.
    /// </summary>
    public Func<TraxDocument, bool>? ById { get; init; }
}

/// <summary>
/// A transport over HTTP POST and a socket. A document goes by its id when <see cref="TraxHttpTransportOptions.ById"/>
/// says so, with no text in the body or the subscribe message; otherwise by its text. Either way it carries
/// <c>operationName</c>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/client-api/dotnet-client">SDK reference</seealso>
public sealed class TraxHttpTransport : ITraxTransport
{
    private readonly TraxHttpTransportOptions _options;

    /// <summary>Builds the transport.</summary>
    /// <exception cref="ArgumentException">
    /// The endpoint is not <c>https</c> on a host that is not loopback, and
    /// <see cref="TraxHttpTransportOptions.AllowInsecureTransport"/> is not set.
    /// </exception>
    public TraxHttpTransport(TraxHttpTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        TransportSecurity.RequireSecure(
            options.Endpoint.IsAbsoluteUri || options.Http?.BaseAddress is null
                ? options.Endpoint
                : new Uri(options.Http!.BaseAddress, options.Endpoint),
            options.AllowInsecureTransport,
            Uri.UriSchemeHttps,
            nameof(options)
        );
        _options = options;
    }

    /// <summary>The body or subscribe payload for a request: by id, or by text.</summary>
    public JsonObject Payload(TraxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = request.Document;
        var byId = document.Id is not null && (_options.ById?.Invoke(document) ?? true);
        var payload = byId
            ? new JsonObject { ["id"] = document.Id }
            : new JsonObject { ["query"] = document.Text };
        payload["operationName"] = document.Name;
        payload["variables"] = request.Variables.DeepClone();
        return payload;
    }

    /// <inheritdoc/>
    public Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    ) =>
        PostAsync(
            _options.Http,
            _options.Endpoint,
            Payload(request),
            _options.MaxResponseSize,
            cancellationToken
        );

    /// <inheritdoc/>
    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_options.Socket is not { } socket)
        {
            sink.Error(
                new InvalidOperationException(
                    "This transport has no socket: give TraxHttpTransportOptions.Socket to follow a subscription."
                )
            );
            return new Disposed();
        }
        return socket.Subscribe(Payload(request), sink);
    }

    /// <summary>
    /// POSTs one GraphQL body and reads the answer. A body with <c>errors</c> is an answer whatever its status, since a
    /// host answers a coded refusal with the status it chose; a success with <c>data</c> is one too. Anything else is a
    /// failure (a status that is not success with no errors among them, whatever data came with it), and so is a
    /// body larger than <paramref name="maxResponseSize"/>, which is refused as it arrives rather than read whole.
    /// </summary>
    public static async Task<TraxAnswer> PostAsync(
        HttpClient http,
        Uri endpoint,
        JsonObject body,
        long maxResponseSize = TraxHttpTransportOptions.DefaultMaxResponseSize,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/graphql-response+json")
        );
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        string? text;
        try
        {
            response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                )
                .ConfigureAwait(false);
            text = await ReadBoundedAsync(response.Content, maxResponseSize, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // A timeout surfaces as a cancellation the caller did not ask for.
            return TraxAnswer.Failed($"The host could not be reached: {ex.Message}");
        }

        using (response)
        {
            if (text is null)
                return TraxAnswer.Failed(
                    $"The server answered with a body larger than {maxResponseSize} bytes.",
                    (int)response.StatusCode
                );
            var status = (int)response.StatusCode;
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                parsed = null;
            }
            // A refusal the host coded (errors) is an answer whatever status it chose; a status that is not success
            // with no errors is a failure, whatever data came with it.
            if (
                TraxAnswer.Of(parsed) is { } answer
                && (response.IsSuccessStatusCode || answer.Errors.Count > 0)
            )
                return answer;
            return TraxAnswer.Failed(
                $"The host answered {$"{status} {response.ReasonPhrase}".TrimEnd()} with no GraphQL answer.",
                status
            );
        }
    }

    // The body as text, or null once it passes the limit, whether or not the host said its length up front.
    private static async Task<string?> ReadBoundedAsync(
        HttpContent content,
        long max,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength > max)
            return null;
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (body.Length + read > max)
                return null;
            body.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);
    }

    private sealed class Disposed : IDisposable
    {
        public void Dispose() { }
    }
}
