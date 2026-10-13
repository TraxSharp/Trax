namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Waits for asynchronous work by polling the condition that means it finished, bounded by a timeout the test owns
/// (central ADRs 0006 and 0014). The caller asserts the result.
/// </summary>
public static class E2EWait
{
    /// <summary>The ceiling a test uses unless it says otherwise.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>True as soon as <paramref name="condition"/> holds; false when the timeout passes first.</summary>
    public static async Task<bool> UntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? DefaultTimeout);
        try
        {
            while (!await condition())
                // determinism: the poll interval of a wait bounded by the timeout above.
                await Task.Delay(25, deadline.Token);
            return true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return false;
        }
    }
}
