using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace Trax.Samples.ApiDashboard.Browser.Fixtures;

/// <summary>
/// What a page did that every test forbids, gathered while the test runs and checked when it ends
/// (<see cref="ConsoleTest"/>): a console error or an uncaught exception, a Content-Security-Policy violation, a
/// request to anywhere but the console's origin, and, on a host that requires persisted operations, a GraphQL
/// request or subscription carrying the text of a document the host registered (Api ADR 0042). It also keeps every
/// GraphQL operation the page sent, for a test to read.
/// </summary>
public sealed class PageChecks
{
    private readonly ConcurrentQueue<string> _problems = new();
    private readonly ConcurrentQueue<GraphQLSent> _sent = new();
    private readonly List<Func<string, bool>> _allowed = [];
    private readonly List<string> _frames = [];
    private readonly List<(Func<string, bool> Match, TaskCompletionSource<string> Seen)> _waiting =
    [];
    private readonly Uri _origin;
    private readonly IReadOnlyDictionary<string, string> _documentIds;
    private readonly bool _persisted;

    private PageChecks(Uri origin, IReadOnlyDictionary<string, string> documentIds, bool persisted)
    {
        _origin = origin;
        _documentIds = documentIds;
        _persisted = persisted;
    }

    /// <summary>Every GraphQL operation the page sent, over HTTP or the socket, in order.</summary>
    public IReadOnlyList<GraphQLSent> Sent => [.. _sent];

    /// <summary>Lets a console error this test expects through, matched on its text.</summary>
    public void AllowConsoleError(Func<string, bool> expected) => _allowed.Add(expected);

    public static async Task<PageChecks> WatchAsync(
        IPage page,
        Uri origin,
        IReadOnlyDictionary<string, string> documentIds,
        bool persisted
    )
    {
        var checks = new PageChecks(origin, documentIds, persisted);
        await page.AddInitScriptAsync(
            """
            window.__cspViolations = [];
            document.addEventListener("securitypolicyviolation", (e) =>
              window.__cspViolations.push(`${e.violatedDirective} ${e.blockedURI}`));
            """
        );
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                checks.Problem($"console error: {message.Text}", console: true);
        };
        page.PageError += (_, error) => checks.Problem($"uncaught: {error}");
        page.Request += (_, request) => checks.OnRequest(request);
        page.WebSocket += (_, socket) =>
        {
            if (!checks.SameOrigin(new Uri(socket.Url)))
                checks.Problem($"socket off the console's origin: {socket.Url}");
            socket.FrameSent += (_, frame) =>
            {
                checks.OnFrame(frame.Text);
                checks.Seen($"sent {frame.Text}");
            };
            socket.FrameReceived += (_, frame) => checks.Seen($"received {frame.Text}");
        };
        return checks;
    }

    /// <summary>
    /// Waits for a socket frame, sent by the page or pushed to it, whose text <paramref name="match"/> accepts; a
    /// frame already seen counts. Each frame's text starts with <c>sent </c> or <c>received </c>.
    /// </summary>
    public Task<string> FrameAsync(Func<string, bool> match, string what)
    {
        lock (_frames)
        {
            var seen = _frames.FirstOrDefault(match);
            if (seen is not null)
                return Task.FromResult(seen);
            var waiter = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _waiting.Add((match, waiter));
            return Bounded(waiter.Task, what);
        }
    }

    /// <summary>
    /// Waits until the page subscribes with the console's registered document for <paramref name="operation"/>,
    /// sent by its id.
    /// </summary>
    public Task<string> SubscribedAsync(string operation, string? alsoContaining = null)
    {
        var id = _documentIds[operation];
        return FrameAsync(
            f =>
                f.StartsWith("sent ", StringComparison.Ordinal)
                && f.Contains("\"subscribe\"")
                && f.Contains(id)
                && (alsoContaining is null || f.Contains(alsoContaining)),
            $"the page subscribes to {operation} by its registered id"
        );
    }

    /// <summary>Waits until the server pushes the page an event of <paramref name="field"/> that holds every text given.</summary>
    public Task<string> PushedAsync(string field, params string[] containing) =>
        FrameAsync(
            f =>
                f.StartsWith("received ", StringComparison.Ordinal)
                && f.Contains("\"next\"")
                && f.Contains($"\"{field}\"")
                && containing.All(f.Contains),
            $"the server pushes {field} with {string.Join(", ", containing)}"
        );

    private static async Task<string> Bounded(Task<string> seen, string what)
    {
        try
        {
            return await seen.WaitAsync(BrowserTimeouts.Server);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"Expected {what} within {BrowserTimeouts.Server.TotalSeconds:0} s."
            );
        }
    }

    private void Seen(string? frame)
    {
        if (frame is null)
            return;
        lock (_frames)
        {
            _frames.Add(frame);
            foreach (var waiter in _waiting.Where(w => w.Match(frame)).ToList())
            {
                _waiting.Remove(waiter);
                waiter.Seen.TrySetResult(frame);
            }
        }
    }

    /// <summary>The problems so far, with the policy violations the page recorded.</summary>
    public async Task<IReadOnlyList<string>> ProblemsAsync(IPage page)
    {
        string[] violations = [];
        try
        {
            violations = await page.EvaluateAsync<string[]>("window.__cspViolations ?? []");
        }
        catch (PlaywrightException)
        {
            // A page that never loaded has nothing to report.
        }
        return [.. violations.Select(v => $"CSP violation: {v}"), .. _problems];
    }

    private void Problem(string problem, bool console = false)
    {
        if (console && _allowed.Any(a => a(problem)))
            return;
        _problems.Enqueue(problem);
    }

    private bool SameOrigin(Uri url) =>
        url.Scheme is "data" or "blob" or "about"
        || (
            string.Equals(url.Host, _origin.Host, StringComparison.OrdinalIgnoreCase)
            && url.Port == _origin.Port
            && url.Scheme is "http" or "ws"
        );

    private void OnRequest(IRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || !SameOrigin(url))
        {
            Problem($"request off the console's origin: {request.Method} {request.Url}");
            return;
        }
        if (request.Method != "POST" || !url.AbsolutePath.EndsWith("/trax/graphql"))
            return;

        var body = request.PostData;
        if (body is null)
            return;
        try
        {
            using var json = JsonDocument.Parse(body);
            Record("http", json.RootElement, body);
        }
        catch (JsonException)
        {
            Problem($"a GraphQL request that is not JSON: {body}");
        }
    }

    private void OnFrame(string? text)
    {
        if (text is null)
            return;
        try
        {
            using var json = JsonDocument.Parse(text);
            if (
                json.RootElement.TryGetProperty("type", out var type)
                && type.GetString() == "subscribe"
                && json.RootElement.TryGetProperty("payload", out var payload)
            )
                Record("socket", payload, text);
        }
        catch (JsonException)
        {
            // Not a graphql-ws message; nothing to check.
        }
    }

    private void Record(string transport, JsonElement operation, string raw)
    {
        var name = operation.TryGetProperty("operationName", out var n) ? n.GetString() : null;
        var text = operation.TryGetProperty("query", out var q) ? q.GetString() : null;
        var id = operation.TryGetProperty("id", out var i) ? i.GetString() : null;
        _sent.Enqueue(new GraphQLSent(transport, name, id, text, raw));

        if (
            _persisted
            && !string.IsNullOrEmpty(text)
            && name is not null
            && _documentIds.ContainsKey(name)
        )
            Problem(
                $"{transport} sent the text of {name}, which the host registered as {_documentIds[name]}: "
                    + "with persisted operations required the console sends its id (Api ADR 0042)"
            );
    }
}

/// <summary>One GraphQL operation the page sent: by <see cref="Id"/>, or as <see cref="Text"/>.</summary>
public sealed record GraphQLSent(
    string Transport,
    string? OperationName,
    string? Id,
    string? Text,
    string Raw
);
