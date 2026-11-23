using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Scoped service that tracks whether it has been disposed.
/// Register as Scoped — scope disposal triggers <see cref="Dispose"/>.
/// </summary>
public class DisposalTracker : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
