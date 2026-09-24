using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Mediator.Configuration;

public partial class TraxMediatorBuilder
{
    /// <summary>
    /// Adds assemblies to scan for <c>IServiceTrain&lt;TIn, TOut&gt;</c> implementations.
    /// </summary>
    public TraxMediatorBuilder ScanAssemblies(params Assembly[] assemblies)
    {
        _assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Sets the DI lifetime for discovered train implementations.
    /// </summary>
    /// <param name="lifetime">The service lifetime (default: Transient)</param>
    public TraxMediatorBuilder TrainLifetime(ServiceLifetime lifetime)
    {
        _lifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Stops the host reading every registered train's chain at startup.
    /// </summary>
    /// <remarks>
    /// The check is on by default and refuses to start when a train's chain cannot run: a
    /// junction whose input never reaches Memory, a chain that ends without the train's return
    /// type, or a <c>Junctions()</c> that does work instead of declaring a chain. There are two
    /// reasons to turn it off. One is the blind spot named in <c>ChainVerification</c>: it knows
    /// the train's declared input type, not the concrete one that flows, so a junction asking for
    /// an interface only a subtype of the input implements reads as a fault. The other is
    /// temporary: a codebase being moved onto <c>Junctions()</c> whose chains do not pass yet.
    /// </remarks>
    public TraxMediatorBuilder SkipChainVerification()
    {
        _skipChainVerification = true;

        return this;
    }

    /// <summary>
    /// Opts the host out of the startup check that fails when trains carry
    /// <c>[TraxAuthorize]</c> but no <c>ITrainAuthorizationService</c> is registered.
    /// Intended for processes that never serve API submissions (e.g. a standalone
    /// scheduler, a dashboard-only host). Do NOT use from an API host.
    /// </summary>
    /// <remarks>
    /// Calling this flips the fail-closed default in
    /// <see cref="Services.TrainExecution.TrainExecutionService"/> to a silent no-op
    /// when the authorization service is missing. Misapplication produces a process
    /// that silently runs authorized trains without any authorization check.
    /// </remarks>
    public TraxMediatorBuilder AllowMissingAuthorizationService()
    {
        _allowMissingAuthorizationService = true;
        return this;
    }

    /// <summary>
    /// Sets the maximum UTF-8 byte length for caller-supplied input JSON in
    /// <c>ITrainExecutionService.RunAsync</c> / <c>QueueAsync</c>. Default is
    /// 256 KiB (262144 bytes). Inputs that exceed the cap are rejected with
    /// <see cref="Exceptions.TrainInputValidationException"/> before any
    /// deserialization runs.
    /// </summary>
    /// <param name="bytes">Maximum accepted size in bytes. Must be positive.</param>
    public TraxMediatorBuilder WithMaxInputJsonBytes(int bytes)
    {
        if (bytes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(bytes),
                bytes,
                "MaxInputJsonBytes must be positive."
            );
        _maxInputJsonBytes = bytes;
        return this;
    }

    /// <summary>
    /// Caps the number of concurrent RUN executions for a single authenticated
    /// principal (keyed by the <c>trax:principal-id</c> claim). Prevents a single
    /// authenticated caller from saturating the global or per-train concurrency
    /// budget via request fan-out. Default is <c>null</c> (no cap).
    /// </summary>
    /// <remarks>
    /// Requires <c>IHttpContextAccessor</c> in DI (registered automatically by
    /// <c>AddTraxApi</c>). Calls made without an HttpContext (scheduler, remote
    /// worker, trusted scope) are not subject to the cap — those paths are
    /// already gated by the global and per-train limits.
    /// </remarks>
    public TraxMediatorBuilder PerPrincipalMaxConcurrentRun(int limit)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                "PerPrincipalMaxConcurrentRun must be positive."
            );
        _perPrincipalMaxConcurrentRun = limit;
        return this;
    }
}
