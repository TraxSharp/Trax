using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>
/// The console's shell: loading a route, the key prompt, the navigation and the footer. Page objects locate by role
/// and accessible name, never by CSS, so the words an operator reads are what the tests read.
/// </summary>
public class ConsolePage(IPage page, Uri origin)
{
    public IPage Page { get; } = page;

    public Uri Origin { get; } = origin;

    /// <summary>The console's address for <paramref name="route"/>.</summary>
    public string Url(string route = "") =>
        new Uri(Origin, "/trax/dashboard/" + route.TrimStart('/')).ToString();

    public ILocator KeyPrompt => Page.GetByLabel("API key");

    public ILocator Footer => Page.GetByTestId("dashboard-footer");

    public ILocator Heading(string name) =>
        Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true });

    public ILocator NavLink(string name) =>
        Page.GetByRole(AriaRole.Navigation)
            .GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

    /// <summary>A table row holding the checkbox the console labels <c>Select #id</c>.</summary>
    public ILocator RowSelectable(long id) =>
        Page.GetByRole(AriaRole.Row)
            .Filter(
                new()
                {
                    Has = Page.GetByRole(
                        AriaRole.Checkbox,
                        new() { Name = $"Select #{id}", Exact = true }
                    ),
                }
            );

    /// <summary>A table row that holds a link named <paramref name="name"/>.</summary>
    public ILocator RowLinked(string name) =>
        Page.GetByRole(AriaRole.Row)
            .Filter(
                new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true }) }
            );

    /// <summary>The console's dialog titled <paramref name="title"/>.</summary>
    public ILocator Dialog(string title) =>
        Page.GetByRole(AriaRole.Dialog, new() { Name = title, Exact = true });

    /// <summary>A toast the console shows, by part of its text.</summary>
    public ILocator Toast(string contains) =>
        Page.GetByRole(AriaRole.Status).Filter(new() { HasText = contains });

    /// <summary>A button on the page, by its accessible name.</summary>
    public ILocator Button(string name) =>
        Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    /// <summary>
    /// Confirms in the console's own dialog, typing <paramref name="typed"/> first where the dialog asks for it.
    /// </summary>
    public async Task ConfirmTypingAsync(string title, string contains, string typed)
    {
        await Dialog(title)
            .GetByLabel($"Type {typed} to confirm", new() { Exact = true })
            .FillAsync(typed, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await ConfirmAsync(title, contains);
    }

    /// <summary>Writes <paramref name="note"/> in the note dialog titled <paramref name="title"/> and sends it.</summary>
    public async Task NoteAsync(string title, string note, string confirm)
    {
        var dialog = Dialog(title);
        await dialog
            .GetByRole(AriaRole.Textbox)
            .FillAsync(note, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = confirm, Exact = true })
            .ClickAsync();
    }

    /// <summary>Waits until the page has sent <paramref name="operation"/>, and returns the request.</summary>
    public Task<IRequest> SendsAsync(string operation) =>
        Page.WaitForRequestAsync(
            r =>
                r.Url.EndsWith("/trax/graphql")
                && r.PostData?.Contains($"\"operationName\":\"{operation}\"") == true,
            new() { Timeout = BrowserTimeouts.Render.Ms() }
        );

    public async Task<IResponse?> GotoAsync(string route = "") => await Page.GotoAsync(Url(route));

    /// <summary>
    /// Signs in through the key prompt, as on every load (the key is never stored). Returns the first GraphQL
    /// request the app sends after it, and the URL of the subscription socket the server acknowledges.
    /// </summary>
    public async Task<Connection> ConnectAsync(Caller caller)
    {
        var acknowledged = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Page.WebSocket += (_, socket) =>
            socket.FrameReceived += (_, frame) =>
            {
                if (frame.Text?.Contains("\"connection_ack\"") == true)
                    acknowledged.TrySetResult(socket.Url);
            };

        await KeyPrompt.FillAsync(caller.Key, new() { Timeout = BrowserTimeouts.Render.Ms() });
        var request = Page.WaitForRequestAsync(
            r => r.Url.EndsWith("/trax/graphql") && r.Method == "POST",
            new() { Timeout = BrowserTimeouts.Render.Ms() }
        );
        await Page.GetByRole(AriaRole.Button, new() { Name = "Connect" }).ClickAsync();
        return new Connection(request, acknowledged.Task);
    }

    /// <summary>Loads <paramref name="route"/> and signs in as <paramref name="caller"/>.</summary>
    public async Task<Connection> OpenAsync(string route, Caller caller)
    {
        await GotoAsync(route);
        return await ConnectAsync(caller);
    }

    /// <summary>
    /// Confirms in the console's own dialog: the one titled <paramref name="title"/>, checking it says
    /// <paramref name="contains"/>, by its confirming button, which carries the action's name.
    /// </summary>
    public async Task ConfirmAsync(string title, string contains)
    {
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = title, Exact = true });
        await Assertions
            .Expect(dialog)
            .ToContainTextAsync(contains, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await dialog.GetByRole(AriaRole.Button, new() { Name = title, Exact = true }).ClickAsync();
    }
}

/// <summary>What signing in started: the first GraphQL request, and the acknowledged socket's URL.</summary>
public sealed record Connection(Task<IRequest> FirstRequest, Task<string> Acknowledged);
