using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Screens;

/// <summary>
/// Shows a bulk operation's progress in the status line until it ends, or until the window closes
/// (<see cref="ConsoleContext.Closed"/>); the host runs it either way.
/// </summary>
internal static class BulkProgress
{
    /// <summary>How often the operation is read.</summary>
    public static TimeSpan Every { get; set; } = TimeSpan.FromSeconds(1);

    public static void Follow(ConsoleContext context, Guid operation, Action ended) =>
        _ = Task.Run(async () =>
        {
            var closed = context.Closed;
            void Say(Action show)
            {
                if (!closed.IsCancellationRequested)
                    context.App.Invoke(() =>
                    {
                        if (!closed.IsCancellationRequested)
                            show();
                    });
            }
            for (; ; )
            {
                TraxAnswer<BulkOperationResult> answer;
                try
                {
                    await Task.Delay(Every, closed);
                    answer = await context.Client.BulkOperationAsync(
                        new BulkOperationVariables { Id = operation, FailuresTake = 0 },
                        closed
                    );
                }
                catch (OperationCanceledException) when (closed.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Say(() => context.Status($"Bulk operation {operation}: {ex.Message}"));
                    return;
                }
                if (answer.Data?.Operations?.BulkOperation is not { } state)
                {
                    Say(() =>
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
                Say(() =>
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
