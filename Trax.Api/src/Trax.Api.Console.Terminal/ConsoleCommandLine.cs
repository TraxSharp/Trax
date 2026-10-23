using System.CommandLine;
using Trax.Api.Console.Terminal.Connection;
using Trax.Api.Console.Terminal.Headless;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Screens;

namespace Trax.Api.Console.Terminal;

/// <summary>
/// <c>trax-console</c>: with no command, the operations console in the terminal; with one, the same lists, actions
/// and streams answered as JSON lines, for scripts and agents. Both send the browser console's documents only.
/// </summary>
internal static class ConsoleCommandLine
{
    /// <summary>
    /// The command line. A test gives <paramref name="open"/> to reach a host of its own, and the writers that stand
    /// for standard output and standard error.
    /// </summary>
    public static RootCommand Create(
        Func<string, string?>? environment = null,
        Func<ConsoleTarget, ConsoleSession>? open = null,
        TextWriter? output = null,
        TextWriter? error = null
    )
    {
        environment ??= Environment.GetEnvironmentVariable;
        open ??= target => ConsoleSession.Open(target);
        var stdout = output ?? System.Console.Out;
        var stderr = error ?? System.Console.Error;
        var host = new Option<string?>("--host")
        {
            Description =
                $"The host serving the operations console, such as https://ops.example.com (or {ConsoleTarget.HostVariable}).",
            Recursive = true,
        };
        var graphQLPath = new Option<string>("--graphql-path")
        {
            Description = "The path its Trax GraphQL endpoint is mapped at.",
            DefaultValueFactory = _ => ConsoleTarget.DefaultGraphQLPath,
            Recursive = true,
        };
        var apiKeyFile = new Option<FileInfo?>("--api-key-file")
        {
            Description =
                $"A file holding the API key the operations gate checks. Or set {ConsoleTarget.ApiKeyVariable}. Never "
                + "on the command line itself.",
            Recursive = true,
        };
        var tokenFile = new Option<FileInfo?>("--token-file")
        {
            Description = $"A file holding a bearer token. Or set {ConsoleTarget.TokenVariable}.",
            Recursive = true,
        };
        var sendText = new Option<bool>("--send-text")
        {
            Description =
                "Send each document's text instead of its id, for a host that runs no persisted operations.",
            Recursive = true,
        };
        var verticalGraphs = new Option<bool>("--vertical-graphs")
        {
            Description =
                "Draw machines top to bottom rather than left to right (r turns them in the console).",
            Recursive = true,
        };
        var anyVersion = new Option<bool>("--any-version")
        {
            Description =
                "Talk to a host of another Trax release anyway. Documents that differ between the two are refused.",
            Recursive = true,
        };

        ConsoleTarget Target(ParseResult p) =>
            ConsoleTarget.Resolve(
                p.GetValue(host),
                p.GetValue(graphQLPath) ?? ConsoleTarget.DefaultGraphQLPath,
                p.GetValue(apiKeyFile),
                p.GetValue(tokenFile),
                p.GetValue(sendText),
                environment
            );

        var root = new RootCommand(
            "The Trax .NET operations console in a terminal. With no command it opens the console; with one it "
                + "answers in JSON lines."
        )
        {
            host,
            graphQLPath,
            apiKeyFile,
            tokenFile,
            sendText,
            anyVersion,
            verticalGraphs,
        };
        root.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    p.GetValue(anyVersion),
                    async (session, version) =>
                        await ConsoleApp.RunAsync(session, version, p.GetValue(verticalGraphs), ct)
                )
        );

        var listName = new Argument<string>("list")
        {
            Description = $"One of {string.Join(", ", Catalog.All.Select(l => l.Name))}.",
        };
        var where = new Option<string[]>("--where")
        {
            Description =
                "A filter as name=value (state=FAILED). Repeatable. An unknown name is refused with the list's filters.",
            AllowMultipleArgumentsPerToken = false,
        };
        var take = new Option<int>("--take")
        {
            Description = "How many rows to read (at most 500).",
            DefaultValueFactory = _ => 25,
        };
        var after = new Option<long?>("--after")
        {
            Description = "The cursor to start after: the nextCursor the previous page printed.",
        };
        var list = new Command("list", "Print a page of a list, one row per line.")
        {
            listName,
            where,
            take,
            after,
        };
        list.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    p.GetValue(anyVersion),
                    (session, _) =>
                        HeadlessCommands.ListAsync(
                            session,
                            p.GetValue(listName)!,
                            Filters(p.GetValue(where)),
                            p.GetValue(take),
                            p.GetValue(after),
                            stdout,
                            stderr,
                            ct
                        )
                )
        );

        var action = new Argument<string>("action")
        {
            Description = "The action, such as requeue.",
        };
        var ids = new Argument<long[]>("ids")
        {
            Description = "The rows' ids.",
            Arity = ArgumentArity.OneOrMore,
        };
        var note = new Option<string?>("--note")
        {
            Description = "The note an action records (acknowledge).",
        };
        var askAfresh = new Option<bool>("--ask-afresh")
        {
            Description =
                "Let a requeued or triggered run ask its decisions again rather than replay them.",
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Print what would be sent, and send nothing.",
        };
        var doCommand = new Command("do", "Run an action on rows of a list, by id.")
        {
            listName,
            action,
            ids,
            note,
            askAfresh,
            dryRun,
        };
        doCommand.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    p.GetValue(anyVersion),
                    (session, _) =>
                        HeadlessCommands.DoAsync(
                            session,
                            p.GetValue(listName)!,
                            p.GetValue(action)!,
                            p.GetValue(ids)!,
                            new ActionOptions(p.GetValue(note), p.GetValue(askAfresh)),
                            p.GetValue(dryRun),
                            stdout,
                            ct
                        )
                )
        );

        var expect = new Option<int>("--expect")
        {
            Description =
                "How many rows the filter matches, as `list` counted them. The host refuses when it now matches another.",
            Required = true,
        };
        var bulk = new Command(
            "bulk",
            "Run an action on every row a filter matches, as one bulk operation the host runs."
        )
        {
            listName,
            action,
            where,
            expect,
            note,
            askAfresh,
            dryRun,
        };
        bulk.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    p.GetValue(anyVersion),
                    (session, _) =>
                        HeadlessCommands.BulkAsync(
                            session,
                            p.GetValue(listName)!,
                            p.GetValue(action)!,
                            Filters(p.GetValue(where)),
                            p.GetValue(expect),
                            new ActionOptions(p.GetValue(note), p.GetValue(askAfresh)),
                            p.GetValue(dryRun),
                            stdout,
                            ct
                        )
                )
        );

        var stream = new Argument<string>("stream")
        {
            Description =
                "runs (every run's state), changes (what changed), run (one run's steps), machines.",
        };
        var id = new Argument<string?>("id")
        {
            Description = "The run's id, for `run`; a machine's name, for `machines`.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var watch = new Command("watch", "Follow a stream, one event per line, until interrupted.")
        {
            stream,
            id,
        };
        watch.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    p.GetValue(anyVersion),
                    (session, _) =>
                        HeadlessCommands.WatchAsync(
                            session,
                            p.GetValue(stream)!,
                            p.GetValue(id),
                            stdout,
                            stderr,
                            ct
                        )
                )
        );

        var run = new Argument<long>("run") { Description = "The run's id." };
        var trace = new Command("trace", "Print a run's steps, decisions and log, one per line.")
        {
            run,
        };
        trace.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    p.GetValue(anyVersion),
                    (session, _) =>
                        HeadlessCommands.TraceAsync(
                            session,
                            p.GetValue(run),
                            stdout,
                            stderr,
                            ct
                        )
                )
        );

        var machineName = new Argument<string?>("machine")
        {
            Description = "The machine; every machine the host serves when left out.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var graph = new Command(
            "graph",
            "Draw a machine's states and transitions as boxes and arrows."
        )
        {
            machineName,
        };
        graph.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    p.GetValue(anyVersion),
                    (session, _) =>
                        HeadlessCommands.GraphAsync(
                            session,
                            p.GetValue(machineName),
                            p.GetValue(verticalGraphs),
                            stdout,
                            stderr,
                            ct
                        )
                )
        );

        var check = new Command(
            "check",
            "Say whether the host serves this console: its Trax release, and whether the credential reaches operations."
        );
        check.SetAction(
            (p, ct) =>
                Run(
                    open,
                    stderr,
                    () => Target(p),
                    anyVersion: true,
                    (session, version) =>
                        HeadlessCommands.CheckAsync(session, version, stdout)
                )
        );

        root.Subcommands.Add(list);
        root.Subcommands.Add(doCommand);
        root.Subcommands.Add(bulk);
        root.Subcommands.Add(watch);
        root.Subcommands.Add(trace);
        root.Subcommands.Add(graph);
        root.Subcommands.Add(check);
        return root;
    }

    // Connects, checks the host's release, and runs the command; a refusal is a message and exit code 1.
    private static async Task<int> Run(
        Func<ConsoleTarget, ConsoleSession> open,
        TextWriter error,
        Func<ConsoleTarget> target,
        bool anyVersion,
        Func<ConsoleSession, VersionCheck, Task<int>> command
    )
    {
        ConsoleSession session;
        try
        {
            session = open(target());
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine(TerminalText.Block(ex.Message));
            return 2;
        }
        await using (session)
        {
            var version = await session.CheckVersionAsync();
            switch (version)
            {
                case VersionCheck.Failed failed when !anyVersion:
                    error.WriteLine(
                        TerminalText.Block(
                            failed.Code is null
                                ? failed.Message
                                : $"{failed.Code}: {failed.Message}"
                        )
                    );
                    return 1;
                case VersionCheck.Other other when !anyVersion:
                    error.WriteLine(TerminalText.Line(other.Message));
                    return 1;
            }
            try
            {
                return await command(session, version);
            }
            catch (InvalidOperationException ex)
            {
                error.WriteLine(ex.Message);
                return 2;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> Filters(string[]? where)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in where ?? [])
        {
            var at = pair.IndexOf('=');
            if (at <= 0)
                throw new InvalidOperationException($"--where '{pair}' is not name=value.");
            filters[pair[..at].Trim()] = pair[(at + 1)..];
        }
        return filters;
    }
}
