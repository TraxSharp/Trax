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

    private sealed record Frame(string Reason, Frame? Previous);

    private sealed class ScopeHandle(Frame frame) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Only pop if this frame is still the current one. Guards against
            // out-of-order disposal across overlapping scopes.
            if (Current.Value == frame)
                Current.Value = frame.Previous;
        }
    }
}
