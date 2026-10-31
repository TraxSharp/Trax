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
    /// <c>Authorization</c>) or a delegating handler, as for any other <see cref="System.Net.Http.HttpClient"/>. Its
    /// <see cref="HttpClient.Timeout"/> bounds each request, its body included. It must not follow redirects: build it
    /// on a <see cref="SocketsHttpHandler"/> or <see cref="HttpClientHandler"/> with
    /// <see cref="SocketsHttpHandler.AllowAutoRedirect"/> off, behind any delegating handlers
    /// (<c>new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })</c>). A client whose handler follows
    /// redirects, <c>new HttpClient()</c> among them, is refused when the transport is built, and so is one on a handler
    /// of another kind unless <see cref="HandlerFollowsNoRedirects"/> is set. The check reads the handler at the end of
    /// the chain, so each delegating handler before it must pass every request on once, to the handler after it, and
    /// send none of its own: one that sends a request again, to the host a redirect names or anywhere else, does what
    /// the check cannot see. An answer from another host than <see cref="Endpoint"/>'s is a
    /// <see cref="TraxAnswer.Failure"/>.
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
    /// Says that <see cref="Http"/>'s handler follows no redirects, for a handler the transport cannot check: one at the
    /// end of its chain that is neither a <see cref="SocketsHttpHandler"/> nor an <see cref="HttpClientHandler"/> (a
    /// test server's, a platform's own). Off by default, and such a client is refused. It changes nothing for a handler
    /// the transport can check: one that follows redirects is refused either way.
    /// </summary>
    public bool HandlerFollowsNoRedirects { get; init; }

    /// <summary>
    /// The largest response body the host may answer with, in bytes; 16 MiB by default. A larger one is a
    /// <see cref="TraxAnswer.Failure"/>, never read into memory. It must be positive.
    /// </summary>
    public long MaxResponseSize { get; init; } = DefaultMaxResponseSize;

    /// <summary>The response body limit when none is set: 16 MiB.</summary>
    public const long DefaultMaxResponseSize = 16 * 1024 * 1024;

    /// <summary>
    /// The socket subscriptions go over, usually a <see cref="GraphQLSubscriptionSocket"/>. Null fails every
    /// subscription at once, with a <see cref="SocketUnavailableException"/>.
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
    /// The endpoint is relative and the client has no base address; or it is not <c>https</c> on a host that is not
    /// loopback, and <see cref="TraxHttpTransportOptions.AllowInsecureTransport"/> is not set; or the client follows
    /// redirects, or its handler is one the transport cannot check and
    /// <see cref="TraxHttpTransportOptions.HandlerFollowsNoRedirects"/> is not set.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="TraxHttpTransportOptions.MaxResponseSize"/> is zero or less.
    /// </exception>
    public TraxHttpTransport(TraxHttpTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireSize(options.MaxResponseSize, "largest response size", nameof(options));
        TransportSecurity.RequireNoRedirects(
            options.Http,
            options.HandlerFollowsNoRedirects,
            nameof(options)
        );
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
            cancellationToken: cancellationToken
        );

    /// <inheritdoc/>
    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_options.Socket is not { } socket)
        {
            sink.Error(
                new SocketUnavailableException(
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
    /// body larger than <paramref name="maxResponseSize"/>, which is refused as it arrives rather than read whole, a
    /// body that breaks off, an answer from another scheme, host or port than <paramref name="endpoint"/>'s (a
    /// redirect the client followed), and a request that outlasts <see cref="HttpClient.Timeout"/> or
    /// <paramref name="requestTimeout"/>, whichever is shorter, body included. Throws only
    /// when <paramref name="cancellationToken"/> is cancelled. Internal: the endpoint is checked for TLS by whoever
    /// built it, a transport or the machine server.
    /// </summary>
    internal static async Task<TraxAnswer> PostAsync(
        HttpClient http,
        Uri endpoint,
        JsonObject body,
        long maxResponseSize = TraxHttpTransportOptions.DefaultMaxResponseSize,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default,
        TimeProvider? time = null
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
        var asked =
            endpoint.IsAbsoluteUri ? endpoint
            : http.BaseAddress is { } baseAddress ? new Uri(baseAddress, endpoint)
            : null;

        // With ResponseHeadersRead the client's timeout stops at the headers; this one runs on through the body, on the
        // clock given.
        var limit = Shorter(http.Timeout, requestTimeout ?? Timeout.InfiniteTimeSpan);
        using var timed = new CancellationTokenSource(limit, time ?? TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timed.Token
        );
        try
        {
            using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token
                )
                .ConfigureAwait(false);
            // A handler the caller vouched for followed a redirect anyway: its answer is not the endpoint's.
            if (
                asked is not null
                && response.RequestMessage?.RequestUri is { IsAbsoluteUri: true } answeredBy
                && Uri.Compare(
                    asked,
                    answeredBy,
                    UriComponents.SchemeAndServer,
                    UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase
                ) != 0
            )
                return TraxAnswer.Failed(
                    $"The request was redirected to {answeredBy.GetLeftPart(UriPartial.Authority)}, which is not "
                        + "the endpoint's host. Build the HttpClient on a handler with automatic redirects off.",
                    (int)response.StatusCode
                );
            var text = await ReadBoundedAsync(response.Content, maxResponseSize, timeout.Token)
                .ConfigureAwait(false);
            return AnswerOf(response, text, maxResponseSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // A timeout surfaces as a cancellation the caller did not ask for.
            return TraxAnswer.Failed(
                $"The host did not answer within {limit.TotalSeconds:0.###} seconds."
            );
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // IOException (HttpIOException among them) is a body that broke off while it was read.
            return TraxAnswer.Failed($"The host could not be reached: {ex.Message}");
        }
    }

    /// <summary>
    /// How deep an answer or a socket message may nest: the deepest any value may (512 levels, the kit's
    /// <c>conformance/limits.json</c>). System.Text.Json's default of 64 counts the envelope around a value, so it would
    /// refuse a context a store wrote at its own limit of 64 once wrapped in <c>data</c> and its fields. Whoever reads
    /// the value inside holds it to its own limit.
    /// </summary>
    internal const int MaxReadDepth = 512;

    internal static readonly JsonDocumentOptions Reading = new() { MaxDepth = MaxReadDepth };

    /// <summary>The longest timeout a request or a connection waits for: the longest a timer can count.</summary>
    internal static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    // Refuses a timeout no timer can wait for: zero or less, other than infinite, or longer than MaxTimeout.
    internal static void RequireTimeout(TimeSpan timeout, string what, string paramName)
    {
        if (
            timeout == Timeout.InfiniteTimeSpan
            || (timeout > TimeSpan.Zero && timeout <= MaxTimeout)
        )
            return;
        throw new ArgumentOutOfRangeException(
            paramName,
            timeout,
            $"The {what} must be positive and at most {MaxTimeout.TotalMilliseconds} milliseconds (about 24.8 days), "
                + "or Timeout.InfiniteTimeSpan."
        );
    }

    // Refuses a size limit no answer fits in: zero or less.
    internal static void RequireSize(long size, string what, string paramName)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(
                paramName,
                size,
                $"The {what} must be at least one byte."
            );
    }

    // Refuses a delay no timer can wait for: one below zero, Timeout.InfiniteTimeSpan among them, or longer than
    // MaxTimeout. Zero waits not at all.
    internal static void RequireDelays(
        IReadOnlyList<TimeSpan>? delays,
        string what,
        string paramName
    )
    {
        foreach (var delay in delays ?? [])
            if (delay < TimeSpan.Zero || delay > MaxTimeout)
                throw new ArgumentOutOfRangeException(
                    paramName,
                    delay,
                    $"Each {what} must be from zero to {MaxTimeout.TotalMilliseconds} milliseconds (about 24.8 days)."
                );
    }

    // The shorter of two timeouts, either of which may be infinite.
    private static TimeSpan Shorter(TimeSpan a, TimeSpan b) =>
        a == Timeout.InfiniteTimeSpan ? b
        : b == Timeout.InfiniteTimeSpan ? a
        : a < b ? a
        : b;

    private static TraxAnswer AnswerOf(HttpResponseMessage response, string? text, long max)
    {
        var status = (int)response.StatusCode;
        if (text is null)
            return TraxAnswer.Failed(
                $"The server answered with a body larger than {max} bytes.",
                status
            );
        TraxAnswer? answer;
        try
        {
            answer = TraxAnswer.Of(JsonNode.Parse(text, documentOptions: Reading));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // Not JSON, or an object with a member twice.
            answer = null;
        }
        // A refusal the host coded (errors) is an answer whatever status it chose; a status that is not success
        // with no errors is a failure, whatever data came with it.
        if (answer is not null && (response.IsSuccessStatusCode || answer.Errors.Count > 0))
            return answer;
        return TraxAnswer.Failed(
            $"The host answered {$"{status} {response.ReasonPhrase}".TrimEnd()} with no GraphQL answer.",
            status
        );
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
