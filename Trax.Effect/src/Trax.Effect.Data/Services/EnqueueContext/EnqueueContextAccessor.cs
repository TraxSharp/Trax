using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Services.EnqueueContext;

/// <inheritdoc />
/// <remarks>
/// The context flows with the async call, not with the instance or its scope. Two enqueues running at once on
/// one scope each see their own, and an enqueue started from inside an <c>OnQueue</c> hook sees
/// its own for as long as it runs and hands the outer one back when it finishes.
/// </remarks>
public class EnqueueContextAccessor : IEnqueueContextAccessor
{
    // Static so that every instance, whatever scope or lifetime resolved it, sees the enqueue
    // running on this async flow. An instance field made a singleton train's accessor, or one
    // resolved from another scope, read null inside its own OnQueue hook.
    private static readonly AsyncLocal<IDataContext?> _current = new();

    /// <inheritdoc />
    public IDataContext? Current => _current.Value;

    /// <inheritdoc />
    public IDisposable Enter(IDataContext context)
    {
        var outer = _current.Value;
        _current.Value = context;

        return new Scope(outer);
    }

    private sealed class Scope(IDataContext? outer) : IDisposable
    {
        public void Dispose() => _current.Value = outer;
    }
}
