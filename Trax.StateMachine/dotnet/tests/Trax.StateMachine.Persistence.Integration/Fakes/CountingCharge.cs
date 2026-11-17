using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>A charge that counts its calls and returns a receipt per call.</summary>
public sealed class CountingCharge : IFeatureCharge
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            string.Create(
                CultureInfo.InvariantCulture,
                $"charge-{Interlocked.Increment(ref _calls)}"
            )
        );
}
