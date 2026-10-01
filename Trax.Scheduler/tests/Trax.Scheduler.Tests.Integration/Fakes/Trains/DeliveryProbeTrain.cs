using System.Collections.Concurrent;
using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A scheduled train that counts how many times its body ran, keyed by
/// <see cref="DeliveryProbeInput.Key"/>, and can be held open until a test releases it, or made
/// to fail. Used to tell a job that was delivered twice from one that ran twice.
/// </summary>
public class DeliveryProbeTrain : ServiceTrain<DeliveryProbeInput, Unit>, IDeliveryProbeTrain
{
    /// <summary>How many times the body ran, per key.</summary>
    public static ConcurrentDictionary<string, int> Runs { get; } = new();

    /// <summary>Set when the body starts, per key.</summary>
    public static ConcurrentDictionary<string, TaskCompletionSource> Started { get; } = new();

    /// <summary>When present for a key, the body waits for it before returning.</summary>
    public static ConcurrentDictionary<string, TaskCompletionSource> Gates { get; } = new();

    /// <summary>The token each body ran with, per key.</summary>
    public static ConcurrentDictionary<string, CancellationToken> Tokens { get; } = new();

    /// <summary>Clears everything recorded for a key.</summary>
    public static void Forget(string key)
    {
        Runs.TryRemove(key, out _);
        Started.TryRemove(key, out _);
        Gates.TryRemove(key, out _);
        Tokens.TryRemove(key, out _);
    }

    protected override Task<Either<Exception, Unit>> Junctions() => Chain<ProbeBody>().Resolve();
}

/// <summary>Input for <see cref="DeliveryProbeTrain"/>.</summary>
public record DeliveryProbeInput : IManifestProperties
{
    public string Key { get; set; } = string.Empty;

    /// <summary>When true, the body throws after counting itself.</summary>
    public bool Fail { get; set; }
}

/// <summary>Interface for <see cref="DeliveryProbeTrain"/>.</summary>
public interface IDeliveryProbeTrain : IServiceTrain<DeliveryProbeInput, Unit> { }

internal sealed class ProbeBody : Junction<DeliveryProbeInput, Unit>
{
    public override async Task<Unit> Run(DeliveryProbeInput input)
    {
        DeliveryProbeTrain.Runs.AddOrUpdate(input.Key, 1, (_, count) => count + 1);
        DeliveryProbeTrain.Tokens[input.Key] = CancellationToken;
        DeliveryProbeTrain
            .Started.GetOrAdd(
                input.Key,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            )
            .TrySetResult();

        if (DeliveryProbeTrain.Gates.TryGetValue(input.Key, out var gate))
            await gate.Task.WaitAsync(CancellationToken);

        if (input.Fail)
            throw new TrainException($"Probe {input.Key} failed as asked.");

        return Unit.Default;
    }
}
