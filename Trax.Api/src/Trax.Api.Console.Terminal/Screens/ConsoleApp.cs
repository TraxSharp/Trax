using System.Diagnostics;
using System.Globalization;
using Terminal.Gui.App;
using Trax.Api.Console.Terminal.Code;
using Trax.Api.Console.Terminal.Connection;
using Trax.Api.Console.Terminal.Lists;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// Runs the console in the terminal until the operator quits. Opening source leaves the terminal to the editor and
/// comes back to the same tab, page and filters when it exits.
/// </summary>
internal static class ConsoleApp
{
    public static async Task<int> RunAsync(
        ConsoleSession session,
        VersionCheck version,
        bool verticalGraphs,
        TimeProvider clock,
        CancellationToken cancellationToken
    )
    {
        var banner = version switch
        {
            VersionCheck.Same same => $"Trax .NET {same.Version}. F1 keys.",
            VersionCheck.Other other =>
                $"This host runs Trax .NET {other.HostVersion}, this console {ConsoleSession.ToolVersion}: documents that differ are refused.",
            VersionCheck.Failed failed => TerminalText.Line(failed.Message),
            _ => "",
        };
        ConsoleState? state = null;
        for (; ; )
        {
            SourceLocation? open = null;
            using (var app = Application.Create())
            {
                app.Init();
                var stop = cancellationToken.Register(() => app.Invoke(app.RequestStop));
                await using var stopDisposal = stop.ConfigureAwait(false);
                // Ends a search for source when the window stops before it is done.
                using var searching = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                var context = new ConsoleContext(
                    app,
                    session.Client,
                    session.Target.Endpoint,
                    clock: clock
                )
                {
                    VerticalGraphs = verticalGraphs,
                };
                var window = new MainWindow(context, state, banner);
                var search = false;
                context.OpenCode = name =>
                {
                    if (search)
                        return;
                    search = true;
                    var root = CodeFinder.Root(Directory.GetCurrentDirectory());
                    context.Status($"Looking for {TerminalText.Line(name)} under {root}...");
                    Ui.Start(
                        app,
                        () => CodeFinder.FindAsync(root, name, searching.Token),
                        found =>
                        {
                            search = false;
                            if (searching.IsCancellationRequested)
                                return;
                            if (found is null)
                            {
                                context.Status(
                                    $"No declaration of {TerminalText.Line(name)} under {root}: run trax-console in the checkout."
                                );
                                return;
                            }
                            open = found;
                            state = window.State;
                            verticalGraphs = context.VerticalGraphs;
                            app.RequestStop(window);
                        },
                        _ => search = false,
                        cancellationToken
                    );
                };
                await app.RunAsync(window, CancellationToken.None).ConfigureAwait(false);
                await searching.CancelAsync().ConfigureAwait(false);
                state ??= window.State;
                window.Dispose();
            }
            if (open is null)
                return 0;

            var editor = CodeFinder.Editor(Environment.GetEnvironmentVariable);
            try
            {
                using var process = Process.Start(CodeFinder.Open(editor, open));
                if (process is not null)
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
                when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                banner = $"Could not start {editor} ({ex.Message}). Set VISUAL or EDITOR.";
                continue;
            }
            banner = string.Create(
                CultureInfo.InvariantCulture,
                $"Back from {Path.GetFileName(open.Path)}:{open.Line}."
            );
        }
    }
}
