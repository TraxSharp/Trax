namespace Trax.Mediator.Services.TrustedExecution;

/// <inheritdoc />
public sealed class TrustedExecutionScope : ITrustedExecutionScope
{
    private static readonly AsyncLocal<Frame?> Current = new();

    public bool IsTrusted => Current.Value is not null;

    public string? CurrentReason => Current.Value?.Reason;

    public IDisposable BeginTrusted(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var previous = Current.Value;
        var frame = new Frame(reason, previous);
        Current.Value = frame;
        return new ScopeHandle(frame);
    }

    /// <summary>
    /// One open scope. A class, not a record: frames are compared by identity, and two scopes
    /// opened with the same reason from the same parent are still different scopes.
    /// </summary>
    private sealed class Frame(string reason, Frame? previous)
    {
        public string Reason { get; } = reason;
        public Frame? Previous { get; } = previous;
        public bool Disposed { get; set; }
    }

    private sealed class ScopeHandle(Frame frame) : IDisposable
    {
        public void Dispose()
        {
            if (frame.Disposed)
                return;
            frame.Disposed = true;

            // A scope disposed while an inner one is still open only marks itself; the inner
            // one skips it when it pops. Popping straight to Previous would hand trust back to
            // a scope that was already disposed.
            if (!ReferenceEquals(Current.Value, frame))
                return;

            var next = frame.Previous;
            while (next is { Disposed: true })
                next = next.Previous;
            Current.Value = next;
        }
    }
}
