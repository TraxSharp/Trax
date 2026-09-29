namespace Trax.Mediator.Services.Principal;

/// <summary>
/// Tells the mediator who the current authenticated caller is, as a stable id. Its only use in
/// Trax is bucketing the <c>PerPrincipalMaxConcurrentRun</c> limit on RUN executions. It is not
/// used for authorization, and nothing here grants or checks access.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddMediator</c> registers a default that always returns <c>null</c>, so the per-principal
/// limit does nothing until a real provider replaces it. Trax.Api's <c>AddTraxApi</c> replaces it
/// with one that reads the id from the current <c>HttpContext</c>. A host outside ASP.NET Core that
/// wants the per-principal limit registers its own as a singleton after <c>AddMediator</c>.
/// </para>
/// <para>
/// The provider is resolved once by the singleton concurrency limiter, so it must be safe to call
/// from any thread. It should read ambient state (the current request) on each call, not capture
/// it. It is called on every RUN when the per-principal limit is set, so keep it cheap.
/// </para>
/// <para>
/// Return an id the caller cannot choose, such as the authenticated user's subject claim. An id
/// taken from a request header or other client input lets a caller spread its runs across many
/// buckets and escape the limit. The limiter keeps one semaphore per distinct id for the life of
/// the process, so the set of ids must stay bounded: never a per-request or random value.
/// </para>
/// </remarks>
public interface ICurrentPrincipalProvider
{
    /// <summary>
    /// The stable identifier of the current authenticated principal, or <c>null</c> when there is
    /// none (a scheduler, a remote worker, an anonymous request). A run with a <c>null</c> or empty
    /// id is not limited per principal, though per-train and global limits still apply. The
    /// comparison is ordinal; the claim it comes from is the host's choice.
    /// </summary>
    string? GetCurrentPrincipalId();
}
