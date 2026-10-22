using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>Shows a bulk operation's progress in the status line until it ends; the host runs it either way.</summary>
internal static class BulkProgress
{
    public static void Follow(ConsoleContext context, Guid operation, Action ended) =>
        _ = Task.Run(async () =>
        {
            for (; ; )
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                var answer = await context.Client.BulkOperationAsync(
                    new BulkOperationVariables { Id = operation, FailuresTake = 0 }
                );
                if (answer.Data?.Operations?.BulkOperation is not { } state)
                {
                    context.App.Invoke(() =>
                        context.Status(
                            $"Bulk operation {operation}: {answer.Message ?? "no answer"}"
                        )
                    );
                    return;
                }
                var line =
                    $"{Wire.Name(state.Kind).ToLowerInvariant()} all: {state.Done} of {state.Total} done"
                    + (state.Failed > 0 ? $", {state.Failed} failed" : "")
                    + $"  ({Wire.Name(state.Status).ToLowerInvariant()})";
                var running = state.Status == BulkOperationStatus.Running;
                context.App.Invoke(() =>
                {
                    context.Status(running ? line : $"{line}  {state.Message}");
                    if (!running)
                        ended();
                });
                if (!running)
                    return;
            }
        });
}
