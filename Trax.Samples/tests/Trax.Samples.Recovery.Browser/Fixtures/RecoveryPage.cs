using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Playwright;

namespace Trax.Samples.Recovery.Browser.Fixtures;

/// <summary>
/// A browser context that serves the built page at the origin it is written for and sends its API calls to the
/// test host.
/// </summary>
/// <remarks>
/// <para>
/// The page is built for the sample's development setup: it is served from <see cref="PageOrigin"/>, which the
/// host's CORS policy admits, and calls the host at <see cref="ApiOrigin"/>. The test host binds a port of its
/// own choosing, so the context fulfils the page's files from the build output and forwards each API request
/// and WebSocket to wherever the host is listening. The page's code runs unchanged, and every request it makes
/// is still seen under the URL it named.
/// </para>
/// <para>
/// A CORS preflight is answered by Playwright itself for a routed request, and the request that follows is
/// forwarded with its <c>Origin</c>, so the host still applies its own policy to it.
/// </para>
/// </remarks>
public sealed class RecoveryPage
{
    /// <summary>Where the page is served from, the origin the host's CORS policy admits.</summary>
    public const string PageOrigin = "http://localhost:5173";

    /// <summary>Where the page calls the host, as the built page names it.</summary>
    public const string ApiOrigin = "http://localhost:5260";

    /// <summary>The GraphQL endpoint as the page names it.</summary>
    public const string GraphQLUrl = ApiOrigin + "/trax/graphql";

    private readonly string _dist;
    private readonly Func<Uri> _host;

    private RecoveryPage(string dist, Func<Uri> host, IBrowserContext context, IPage page)
    {
        _dist = dist;
        _host = host;
        Context = context;
        Page = page;
    }

    public IBrowserContext Context { get; }

    public IPage Page { get; }

    /// <summary>Every GraphQL request the page sent over HTTP, in order.</summary>
    public List<IRequest> GraphQLRequests { get; } = [];

    /// <summary>
    /// Opens a context and a page with the routes in place. <paramref name="host"/> is read on every request,
    /// so a test that restarts the host keeps its page.
    /// </summary>
    /// <param name="browser">The run's browser.</param>
    /// <param name="dist">The built page, from <see cref="RecoveryClientBuild"/>.</param>
    /// <param name="host">The test host's address.</param>
    /// <param name="replace">Rewrites the text of a built file before it is served, or null to serve it as built.</param>
    public static async Task<RecoveryPage> OpenAsync(
        IBrowser browser,
        string dist,
        Func<Uri> host,
        Func<string, string, string>? replace = null
    )
    {
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var opened = new RecoveryPage(dist, host, context, page);

        await context.RouteAsync($"{PageOrigin}/**", route => opened.ServeFile(route, replace));
        await context.RouteAsync($"{ApiOrigin}/**", route => opened.Forward(route));
        await context.RouteWebSocketAsync(
            $"{ApiOrigin.Replace("http", "ws", StringComparison.Ordinal)}/**",
            route => WebSocketBridge.Open(route, opened.ToHost(route.Url, "ws"))
        );
        page.Console += (_, message) =>
        {
            lock (opened.Diagnostics)
                opened.Diagnostics.Add($"console {message.Type}: {message.Text}");
        };
        page.PageError += (_, error) =>
        {
            lock (opened.Diagnostics)
                opened.Diagnostics.Add($"uncaught: {error}");
        };
        context.RequestFailed += (_, request) =>
        {
            lock (opened.Diagnostics)
                opened.Diagnostics.Add($"failed {request.Method} {request.Url}: {request.Failure}");
        };
        context.Request += (_, request) =>
        {
            if (request.Url.StartsWith(GraphQLUrl, StringComparison.Ordinal))
                lock (opened.GraphQLRequests)
                    opened.GraphQLRequests.Add(request);
        };
        return opened;
    }

    /// <summary>Loads the page and opens the topic map wizard.</summary>
    public async Task OpenTheWizardAsync()
    {
        await Page.GotoAsync(PageOrigin + "/");
        try
        {
            await ShowTheTopicMapAsync(Page);
            await Assertions
                .Expect(WizardState)
                .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Render.Ms() });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException(
                "The wizard did not render. The page says:\n"
                    + await Page.Locator("body").InnerTextAsync()
                    + "\nIts console and failed requests:\n"
                    + string.Join("\n", Diagnostics),
                ex
            );
        }
    }

    /// <summary>
    /// Shows the topic map on a loaded page: the page opens on the food order, so the Workflows view is picked
    /// first, then its Topic map scenario.
    /// </summary>
    public static async Task ShowTheTopicMapAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Workflows" })
            .ClickAsync(new() { Timeout = BrowserTimeouts.Render.Ms() });
        await page.GetByRole(AriaRole.Tab, new() { Name = "Topic map" })
            .ClickAsync(new() { Timeout = BrowserTimeouts.Render.Ms() });
    }

    /// <summary>What the page logged and which of its requests failed, for a harness failure's message.</summary>
    public List<string> Diagnostics { get; } = [];

    /// <summary>The wizard's current step, as the page labels it.</summary>
    public ILocator WizardState => Page.Locator(".wizard-state");

    /// <summary>The draft id the page shows, its first eight characters.</summary>
    public ILocator DraftId => Page.Locator(".wizard-step code");

    /// <summary>The problem the wizard shows, when the server refused a step.</summary>
    public ILocator Problem => Page.Locator(".hint.error");

    /// <summary>The review deadline Built shows, from the due time the server sends beside the snapshot.</summary>
    public ILocator ReviewDeadline => Page.Locator(".review-deadline");

    /// <summary>The GraphQL requests sent since <paramref name="from"/>, whose body names <paramref name="field"/>.</summary>
    public List<IRequest> RequestsNaming(string field, int from = 0)
    {
        lock (GraphQLRequests)
            return GraphQLRequests
                .Skip(from)
                .Where(r => (r.PostData ?? "").Contains(field, StringComparison.Ordinal))
                .ToList();
    }

    private async Task ServeFile(IRoute route, Func<string, string, string>? replace)
    {
        var path = new Uri(route.Request.Url).AbsolutePath.TrimStart('/');
        var file = Path.Combine(_dist, path);
        if (path.Length == 0 || !File.Exists(file))
            file = Path.Combine(_dist, "index.html");

        if (replace is null || !file.EndsWith(".js", StringComparison.Ordinal))
        {
            await route.FulfillAsync(new() { Path = file });
            return;
        }
        await route.FulfillAsync(
            new()
            {
                ContentType = "text/javascript",
                Body = replace(path, await File.ReadAllTextAsync(file)),
            }
        );
    }

    // The request is sent from the test process and its answer fulfilled into the page, rather than continued
    // from the browser: chromium refuses a page it was handed by a route a request into the loopback address
    // space, which the host listens on. The request keeps its Origin, so the host's CORS policy still decides.
    private async Task Forward(IRoute route)
    {
        try
        {
            var response = await route.FetchAsync(
                new() { Url = ToHost(route.Request.Url, "http").ToString() }
            );
            await route.FulfillAsync(new() { Response = response });
        }
        catch (PlaywrightException)
        {
            // The host is down (a test kills it on purpose): the page sees a failed request, as it would.
            await route.AbortAsync("connectionrefused");
        }
    }

    private Uri ToHost(string url, string scheme)
    {
        var original = new Uri(url);
        var host = _host();
        return new UriBuilder(original)
        {
            Scheme = scheme,
            Host = host.Host,
            Port = host.Port,
        }.Uri;
    }

    /// <summary>
    /// Joins the page's WebSocket to one opened to the host, frame for frame. Playwright's own
    /// <c>ConnectToServer</c> reaches only the URL the page named.
    /// </summary>
    private sealed class WebSocketBridge
    {
        private readonly IWebSocketRoute _page;
        private readonly ClientWebSocket _host = new();
        private readonly Channel<string> _toHost = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource _closed = new();

        private WebSocketBridge(IWebSocketRoute page) => _page = page;

        public static void Open(IWebSocketRoute page, Uri host)
        {
            var bridge = new WebSocketBridge(page);
            page.OnMessage(frame => bridge._toHost.Writer.TryWrite(frame.Text ?? ""));
            page.OnClose((_, _) => bridge._closed.Cancel());
            _ = bridge.RunAsync(host);
        }

        private async Task RunAsync(Uri host)
        {
            try
            {
                // graphql-ws speaks this subprotocol, which the host requires on the upgrade.
                _host.Options.AddSubProtocol("graphql-transport-ws");
                await _host.ConnectAsync(host, _closed.Token);
                await Task.WhenAll(PumpToHost(), PumpToPage());
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                // The page closed its socket, or the host went away (a test kills it on purpose).
            }
            finally
            {
                try
                {
                    await _page.CloseAsync();
                }
                catch (PlaywrightException)
                {
                    // The page is already gone.
                }
                _host.Dispose();
            }
        }

        private async Task PumpToHost()
        {
            await foreach (var text in _toHost.Reader.ReadAllAsync(_closed.Token))
                await _host.SendAsync(
                    Encoding.UTF8.GetBytes(text),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    _closed.Token
                );
        }

        private async Task PumpToPage()
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            // However the host's side ends (closed, or gone with its process), the page's side is closed too, so the
            // page sees its socket drop as it would with a real host.
            try
            {
                while (!_closed.IsCancellationRequested)
                {
                    var received = await _host.ReceiveAsync(buffer, _closed.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        break;
                    message.Write(buffer, 0, received.Count);
                    if (!received.EndOfMessage)
                        continue;
                    _page.Send(
                        Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)
                    );
                    message.SetLength(0);
                }
            }
            finally
            {
                _closed.Cancel();
            }
        }
    }
}
