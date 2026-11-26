namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>How many sandboxes run at once. A request past the cap is refused at once, not queued, so a hub cannot pile work up on the host.</summary>
internal sealed class SandboxGate(int max)
{
    private int _running;

    public int Running => Volatile.Read(ref _running);

    /// <summary>A slot, released when disposed; null when every slot is taken.</summary>
    public IDisposable? TryEnter()
    {
        if (Interlocked.Increment(ref _running) <= max)
            return new Slot(this);
        Interlocked.Decrement(ref _running);
        return null;
    }

    private sealed class Slot(SandboxGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Interlocked.Decrement(ref gate._running);
        }
    }
}
