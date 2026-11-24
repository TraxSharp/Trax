using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// Runs a train in a fresh sandbox of its own, through the sandbox launcher, and waits for it.
/// Registered by <c>AddTraxSandbox</c>; inject it into the hub junction that hands work out.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
public interface ITraxSandbox
{
    /// <summary>
    /// Sends <paramref name="input"/> to a sandbox that runs <typeparamref name="TTrain"/>, with
    /// the profile and limits its class declares with <see cref="TraxSandboxAttribute"/>, and
    /// returns its output and files.
    /// </summary>
    /// <typeparam name="TTrain">The train's interface, registered on this host as well as in the profile's image.</typeparam>
    /// <typeparam name="TInput">Its input type.</typeparam>
    /// <typeparam name="TOutput">Its output type.</typeparam>
    /// <param name="input">The input. It reaches the sandbox, and nothing else from this host does.</param>
    /// <param name="ct">Cancels the wait. The launcher still kills the sandbox at its timeout.</param>
    /// <exception cref="SandboxRunException">The run did not complete, with why.</exception>
    /// <exception cref="Trax.Core.Exceptions.TrainException">The train ran and failed in the sandbox.</exception>
    /// <exception cref="InvalidOperationException">The train is not registered here, or does not carry <see cref="TraxSandboxAttribute"/>.</exception>
    Task<SandboxRun<TOutput>> RunAsync<TTrain, TInput, TOutput>(
        TInput input,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : notnull;
}
