namespace Trax.Mediator.Services.TrustedExecution;

/// <summary>
/// Default <see cref="ITrustedExecutionScope"/>, registered as a singleton by <c>AddMediator</c>.
/// Its state is a static <c>AsyncLocal</c>, so every instance sees the same scopes: a new instance
/// is not a fresh, untrusted one. Infrastructure; resolve <see cref="ITrustedExecutionScope"/>
/// instead of constructing it. Everything <see cref="ITrustedExecutionScope"/> says about what
/// trust bypasses applies.
/// </summary>
public sealed class TrustedExecutionScope : ITrustedExecutionScope
{
    private static readonly AsyncLocal<Frame?> Current = new();

    /// <inheritdoc/>
    public bool IsTrusted => Active is not null;

    /// <inheritdoc/>
    public string? CurrentReason => Active?.Reason;

    /// <summary>
    /// The nearest scope on this flow that is not disposed. A flow that captured a scope (a task
    /// started inside it and not awaited) still holds that frame after the owning flow disposes
    /// it, so the frame's own flag is what ends trust there, not the owner's pop.
    /// </summary>
    private static Frame? Active
    {
        get
        {
            var frame = Current.Value;
            while (frame is { Disposed: true })
                frame = frame.Previous;
            return frame;
        }
    }

    /// <inheritdoc/>
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
        // Volatile: a flow that captured this frame may read the flag on another thread from
        // the one that disposed it.
        private volatile bool _disposed;

        public string Reason { get; } = reason;
        public Frame? Previous { get; } = previous;

        public bool Disposed
        {
            get => _disposed;
            set => _disposed = value;
        }
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
