using Trax.Effect.Services.ChangeSignal;

namespace Trax.Scheduler.Tests.Integration.Fakes;

/// <summary>
/// Test double for <see cref="ITraxChangeSignal"/> that records the domains a write path signals,
/// so emission tests can assert exactly which domain(s) fired.
/// </summary>
public sealed class RecordingChangeSignal : ITraxChangeSignal
{
    private readonly List<ChangeDomain> _domains = new();

    public void Notify(ChangeDomain domain)
    {
        lock (_domains)
            _domains.Add(domain);
    }

    public IReadOnlyList<ChangeDomain> Domains
    {
        get
        {
            lock (_domains)
                return _domains.ToList();
        }
    }

    public void Clear()
    {
        lock (_domains)
            _domains.Clear();
    }
}
