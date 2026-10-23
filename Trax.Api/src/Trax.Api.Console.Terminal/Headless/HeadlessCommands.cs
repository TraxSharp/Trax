using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Connection;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Api.Console.Terminal.Trace;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Headless;

/// <summary>
/// The console without a terminal: each command prints JSON, one value per line, on standard output, and says what
/// went wrong on standard error with exit code 1. A row is printed as the host wrote it.
/// </summary>
internal static class HeadlessCommands
{
    /// <summary>How often a bulk operation's progress is read while it runs.</summary>
    internal static TimeSpan BulkPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public static async Task<int> ListAsync(
        ConsoleSession session,
        string name,
        IReadOnlyDictionary<string, string> filters,
        int take,
        long? after,
        TextWriter output,
        TextWriter error,
        CancellationToken ct
    )
    {
        var list = Catalog.Named(name);
        RefuseUnknownFilters(list, filters);
        if (take is < 1 or > 500)
            throw new InvalidOperationException("--take is between 1 and 500.");
        var answer = await list.LoadAsync(session.Client, new ListQuery(take, after, filters), ct);
        if (answer.Data is not { } page)
            return Refused(answer, error);
        foreach (var row in page.Rows)
            await output.WriteLineAsync(row.Json?.ToJsonString() ?? "null");
        await error.WriteLineAsync(
            new JsonObject
            {
                ["totalCount"] = page.TotalCount,
                ["estimated"] = page.Estimated,
                ["nextCursor"] = page.NextCursor,
            }.ToJsonString()
        );
        return 0;
    }

    public static async Task<int> DoAsync(
        ConsoleSession session,
        string name,
        string action,
        IReadOnlyList<long> ids,
        ActionOptions options,
        bool dryRun,
        TextWriter output,
        CancellationToken ct
    )
    {
        var list = Catalog.Named(name);
        var info =
            list.ActionInfos.FirstOrDefault(a => a.Name == action)
            ?? throw new InvalidOperationException(
                $"{list.Name} has no action '{action}'. It has {string.Join(", ", list.ActionInfos.Select(a => a.Name))}."
            );
        if (info.NeedsNote && string.IsNullOrWhiteSpace(options.Note))
            throw new InvalidOperationException($"{action} records a note: give it with --note.");
        var line = new JsonObject
        {
            ["list"] = list.Name,
            ["action"] = action,
            ["ids"] = new JsonArray(ids.Select(i => (JsonNode)i).ToArray()),
        };
        if (dryRun)
        {
            line["dryRun"] = true;
            await output.WriteLineAsync(line.ToJsonString());
            return 0;
        }
        var outcome = await list.RunAsync(session.Client, action, ids, options, ct);
        line["succeeded"] = outcome.Succeeded;
        line["message"] = outcome.Message;
        if (outcome.Code is not null)
            line["code"] = outcome.Code;
        await output.WriteLineAsync(line.ToJsonString());
        return outcome.Succeeded ? 0 : 1;
    }

    public static async Task<int> BulkAsync(
        ConsoleSession session,
        string name,
        string action,
        IReadOnlyDictionary<string, string> filters,
        int expect,
        ActionOptions options,
        bool dryRun,
        TextWriter output,
        CancellationToken ct
    )
    {
        var list = Catalog.Named(name);
        RefuseUnknownFilters(list, filters);
        var bulk =
            list.BulkActions.FirstOrDefault(b => b.Name == action)
            ?? throw new InvalidOperationException(
                list.BulkActions.Count == 0
                    ? $"{list.Name} has no bulk actions."
                    : $"{list.Name} has no bulk action '{action}'. It has {string.Join(", ", list.BulkActions.Select(b => b.Name))}."
            );
        if (bulk.NeedsNote && string.IsNullOrWhiteSpace(options.Note))
            throw new InvalidOperationException($"{action} records a note: give it with --note.");
        var line = new JsonObject
        {
            ["list"] = list.Name,
            ["action"] = action,
            ["expect"] = expect,
            ["filter"] = Wire.Json(bulk.Filter(filters)),
        };
        if (dryRun)
        {
            line["dryRun"] = true;
            await output.WriteLineAsync(line.ToJsonString());
            return 0;
        }
        var (outcome, operation) = await Catalog.StartBulkAsync(
            session.Client,
            bulk,
            filters,
            expect,
            options,
            ct
        );
        line["succeeded"] = outcome.Succeeded;
        line["message"] = outcome.Message;
        if (outcome.Code is not null)
            line["code"] = outcome.Code;
        line["operation"] = operation?.ToString();
        await output.WriteLineAsync(line.ToJsonString());
        if (!outcome.Succeeded || operation is not { } id)
            return outcome.Succeeded ? 0 : 1;

        // Its progress until it ends: the host runs it whether this process stays or not.
        for (; ; )
        {
            await Task.Delay(BulkPollInterval, ct);
            var progress = await session.Client.BulkOperationAsync(
                new BulkOperationVariables { Id = id, FailuresTake = 0 },
                ct
            );
            if (progress.Data?.Operations?.BulkOperation is not { } state)
            {
                await output.WriteLineAsync(Error(progress.Message, progress.Code).ToJsonString());
                return 1;
            }
            await output.WriteLineAsync(
                new JsonObject
                {
                    ["operation"] = id.ToString(),
                    ["status"] = Wire.Name(state.Status),
                    ["done"] = state.Done,
                    ["failed"] = state.Failed,
                    ["total"] = state.Total,
                    ["message"] = state.Message,
                }.ToJsonString()
            );
            if (state.Status != BulkOperationStatus.Running)
                return state.Status == BulkOperationStatus.Finished && state.Failed == 0 ? 0 : 1;
        }
    }

    public static async Task<int> WatchAsync(
        ConsoleSession session,
        string stream,
        string? id,
        TextWriter output,
        TextWriter error,
        CancellationToken ct
    )
    {
        var ended = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var gate = new object();
        void Write(TextWriter to, string line)
        {
            lock (gate)
            {
                to.WriteLine(line);
                to.Flush();
            }
        }
        void On<T>(TraxStreamEvent<T> e, Func<T, JsonNode?> data)
        {
            switch (e)
            {
                case TraxStreamEvent<T>.Next next:
                    Write(output, data(next.Data)?.ToJsonString() ?? "null");
                    break;
                case TraxStreamEvent<T>.Interrupted interrupted:
                    Write(
                        error,
                        new JsonObject { ["interrupted"] = interrupted.Reason }.ToJsonString()
                    );
                    break;
                case TraxStreamEvent<T>.Resumed:
                    Write(error, new JsonObject { ["resumed"] = true }.ToJsonString());
                    break;
                case TraxStreamEvent<T>.Ended over:
                    var message =
                        over.Failure?.Message
                        ?? string.Join("; ", over.Errors.Select(x => x.Message));
                    Write(
                        error,
                        Error(
                                message.Length == 0 ? "The host ended the stream." : message,
                                over.Errors.FirstOrDefault()?.Code
                            )
                            .ToJsonString()
                    );
                    ended.TrySetResult(over.Errors.Count == 0 && over.Failure is null ? 0 : 1);
                    break;
            }
        }

        var client = session.Client;
        using var subscription = stream switch
        {
            "runs" => client.OnTrainStateChanged(e => On(e, d => Wire.Json(d.OnTrainStateChanged))),
            "changes" => client.OnDataChanged(e => On(e, d => Wire.Json(d.OnDataChanged))),
            "run" => client.OnJunctionEvent(
                new OnJunctionEventVariables
                {
                    MetadataId = Wire.Long(
                        "run",
                        id ?? throw new InvalidOperationException("watch run needs the run's id.")
                    ),
                },
                e => On(e, d => Wire.Json(d.OnJunctionEvent))
            ),
            "machines" => client.OnMachineInstanceChanged(
                new OnMachineInstanceChangedVariables { Machine = id },
                e => On(e, d => Wire.Json(d.OnMachineInstanceChanged))
            ),
            _ => throw new InvalidOperationException(
                $"'{stream}' is not a stream. Use runs, changes, run <id> or machines [name]."
            ),
        };
        using var stop = ct.Register(() => ended.TrySetResult(0));
        return await ended.Task;
    }

    public static async Task<int> TraceAsync(
        ConsoleSession session,
        long run,
        TextWriter output,
        CancellationToken ct
    )
    {
        var answer = await RunTrace.LoadAsync(session.Client, run, ct);
        if (answer.Data is not { } entries)
            return Refused(answer, System.Console.Error);
        foreach (var entry in entries)
            await output.WriteLineAsync(
                new JsonObject
                {
                    ["kind"] = entry.Kind.ToString().ToLowerInvariant(),
                    ["at"] = entry.At?.ToString("O"),
                    ["text"] = entry.Text,
                    ["data"] = entry.Json?.DeepClone(),
                }.ToJsonString()
            );
        return 0;
    }

    /// <summary>Prints each machine's graph as boxes and arrows, or the one named; plain text, not JSON.</summary>
    public static async Task<int> GraphAsync(
        ConsoleSession session,
        string? machine,
        bool vertical,
        TextWriter output,
        CancellationToken ct
    )
    {
        var answer = await session.Client.MachinesAsync(ct);
        if (answer.Data?.Operations?.Machines is not { } machines)
            return Refused(answer, System.Console.Error);
        var shown = machines
            .Where(m => machine is null ? !m.SystemOwned : m.Name == machine)
            .OrderBy(m => m.Name)
            .ToList();
        if (shown.Count == 0)
            throw new InvalidOperationException(
                machine is null
                    ? "This host serves no machines."
                    : $"This host serves no machine '{machine}'. It serves {string.Join(", ", machines.Select(m => m.Name))}."
            );
        foreach (var m in shown)
        {
            await output.WriteLineAsync($"{m.Name} v{m.Version}");
            await output.WriteLineAsync();
            foreach (var line in Machines.MachineGraph.Lines(m, vertical: vertical))
                await output.WriteLineAsync(line);
            await output.WriteLineAsync();
        }
        return 0;
    }

    public static async Task<int> CheckAsync(
        ConsoleSession session,
        VersionCheck version,
        TextWriter output
    )
    {
        var line = new JsonObject
        {
            ["host"] = session.Target.Endpoint.ToString(),
            ["tool"] = ConsoleSession.ToolVersion,
        };
        switch (version)
        {
            case VersionCheck.Same same:
                line["hostVersion"] = same.Version;
                line["serves"] = true;
                break;
            case VersionCheck.Other other:
                line["hostVersion"] = other.HostVersion;
                line["serves"] = false;
                line["message"] = other.Message;
                break;
            case VersionCheck.Failed failed:
                line["serves"] = false;
                line["message"] = failed.Message;
                if (failed.Code is not null)
                    line["code"] = failed.Code;
                break;
        }
        await output.WriteLineAsync(line.ToJsonString());
        return version is VersionCheck.Same ? 0 : 1;
    }

    private static void RefuseUnknownFilters(
        IConsoleList list,
        IReadOnlyDictionary<string, string> filters
    )
    {
        foreach (var (name, value) in filters)
        {
            var filter =
                list.Filters.FirstOrDefault(f => f.Name == name)
                ?? throw new InvalidOperationException(
                    $"{list.Name} has no filter '{name}'. It has {string.Join(", ", list.Filters.Select(f => f.Name))}."
                );
            if (
                filter.Choices is { } choices
                && !choices.Contains(value, StringComparer.OrdinalIgnoreCase)
            )
                throw new InvalidOperationException(
                    $"{name} '{value}' is not one of {string.Join(", ", choices)}."
                );
        }
    }

    private static int Refused<T>(TraxAnswer<T> answer, TextWriter error)
    {
        error.WriteLine(
            Error(answer.Message ?? "The host refused it.", answer.Code).ToJsonString()
        );
        return 1;
    }

    private static JsonObject Error(string? message, string? code)
    {
        var error = new JsonObject { ["error"] = message ?? "The host refused it." };
        if (code is not null)
            error["code"] = code;
        return error;
    }
}
