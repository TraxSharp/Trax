using Trax.Samples.Shared.Testing;

namespace Trax.Samples.ApiDashboard.Browser.Fixtures;

/// <summary>Waits on a condition the host's state reaches, bounded, and fails saying what never happened.</summary>
public static class Wait
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var reached = await Polling.WaitUntilAsync(condition, timeout, Poll);
        reached.Should().BeTrue($"{what} within {timeout.TotalSeconds:0} s");
    }

    /// <summary>Waits until <paramref name="read"/> returns a value, and returns it.</summary>
    public static async Task<T> ForAsync<T>(Func<Task<T?>> read, TimeSpan timeout, string what)
        where T : class
    {
        T? found = null;
        await UntilAsync(async () => (found = await read()) is not null, timeout, what);
        return found!;
    }
}
