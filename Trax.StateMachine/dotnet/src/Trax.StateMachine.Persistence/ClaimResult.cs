using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using EffectClaim = Trax.Effect.Models.EffectClaim.EffectClaim;

namespace Trax.StateMachine.Persistence;

/// <summary>The outcome of claiming an effect key: won (with the fence token to complete/release under), or lost.</summary>
public abstract record ClaimResult
{
    /// <summary>
    /// This caller holds the claim: it inserted the key, or reclaimed an in-flight claim whose lease had expired.
    /// Run the effect, then pass <c>OwnerToken</c> to <see cref="IEffectClaimStore.Complete"/> or
    /// <see cref="IEffectClaimStore.ReleaseOwned"/>.
    /// </summary>
    /// <param name="OwnerToken">
    /// The fence token minted for this claim. Completing or releasing succeeds only while the row still carries
    /// it, so a claimant whose lease was taken over cannot touch the new owner's row.
    /// </param>
    public sealed record Won(Guid OwnerToken) : ClaimResult;

    /// <summary>
    /// Another caller holds the key: either its claim is still inside its lease, or the effect already completed.
    /// Read <see cref="IEffectClaimStore.GetReceipt"/> to tell the two apart.
    /// </summary>
    public sealed record Lost : ClaimResult;

    private ClaimResult() { }
}
