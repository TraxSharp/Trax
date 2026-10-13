using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// For a host whose <see cref="ISnapshotStore"/> is not the Trax store, and so keeps the effect-claim ledger: the effect
/// claims of a user's draft about to be deleted, held so that no effect starts on the draft while it goes,
/// and so that only claims whose outcome is settled are released with it. A discard and the draft sweep take one
/// before their delete: <see cref="Take"/> claims every key the draft has that nobody holds, and refuses when one is
/// live. After the delete, <see cref="Release"/> frees the held keys and the settled ones in the delete's
/// transaction; when the delete does not happen, <see cref="Drop"/> frees the held keys alone.
/// </summary>
internal sealed class DraftClaimHold
{
    /// <summary>
    /// How long a hold keeps a key. A discard or a sweep releases it within one round trip; a host that dies holding
    /// one leaves it in flight this long, then the next caller reclaims it.
    /// </summary>
    internal static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    private readonly IEffectClaimStore? _claims;
    private readonly List<(string Key, Guid Token)> _held = [];
    private readonly List<(string Key, string Receipt)> _settled = [];

    private DraftClaimHold(IEffectClaimStore? claims) => _claims = claims;

    /// <summary>
    /// Holds every one of <paramref name="keys"/>, or returns null, holding nothing, when one of them is live. A key
    /// nobody holds, or one whose lease passed with no receipt, is claimed for the hold. A key whose effect is in
    /// flight within its lease is live. A key whose effect completed is live only while its result may still be on
    /// its way to the draft: the draft is in the effect's from-state (<see cref="EffectKey.From"/>; an unknown one
    /// counts as that), does not hold its receipt (<paramref name="recorded"/>), and the claim's lease has not passed.
    /// Otherwise it is settled.
    /// </summary>
    /// <param name="claims">The claim ledger; null holds nothing and refuses nothing.</param>
    /// <param name="keys">The draft's effect keys, each with the state its effect runs from.</param>
    /// <param name="state">The state the draft is in, as it was read; null when it cannot be read.</param>
    /// <param name="recorded">Whether the draft holds a given receipt.</param>
    internal static async Task<DraftClaimHold?> Take(
        IEffectClaimStore? claims,
        IEnumerable<EffectKey> keys,
        string? state,
        Func<string, bool> recorded
    )
    {
        var hold = new DraftClaimHold(claims);
        if (claims is null)
            return hold;
        foreach (var (key, from) in keys)
        {
            if (
                await claims.TryClaim(key, Lease, contentFingerprint: null, CancellationToken.None)
                is ClaimResult.Won won
            )
            {
                hold._held.Add((key, won.OwnerToken));
                continue;
            }
            if (
                await claims.GetCompleted(key, CancellationToken.None) is { } completed
                && (
                    recorded(completed.Receipt)
                    || from is not null && state is not null && state != from
                    || claims is EfEffectClaimStore ef && await ef.LeasePassed(key)
                )
            )
            {
                hold._settled.Add((key, completed.Receipt));
                continue;
            }
            await hold.Drop();
            return null;
        }
        return hold;
    }

    /// <summary>
    /// One effect claim key of a draft, with the state its effect runs from, or null when that is not known. A
    /// completed claim's result can only be on its way to a draft in that state: the effect's commit writes only the
    /// draft as it loaded it there.
    /// </summary>
    /// <param name="Key">The intent key.</param>
    /// <param name="From">The state the effect runs from, or null when it is not known.</param>
    internal readonly record struct EffectKey(string Key, string? From);

    /// <summary>Frees the keys this hold claimed, leaving every other claim as it is. For a delete that did not happen.</summary>
    internal async Task Drop()
    {
        if (_claims is null)
            return;
        foreach (var (key, token) in _held)
            await _claims.ReleaseOwned(key, token, CancellationToken.None);
    }

    /// <summary>
    /// Frees the keys this hold claimed and the settled claims it read, each only while it is as it was read, so the
    /// deleted draft's next incarnation starts from clean keys. For a delete that happened, inside its transaction.
    /// </summary>
    internal async Task Release()
    {
        if (_claims is null)
            return;
        await Drop();
        foreach (var (key, receipt) in _settled)
            await _claims.ReleaseForReset(
                key,
                r => string.Equals(r, receipt, StringComparison.Ordinal),
                CancellationToken.None
            );
    }

    /// <summary>
    /// Whether a draft holds a given receipt: the receipt its effect's reducer records, under <c>receipt</c> in its
    /// context. A string elsewhere in the context is the client's, never a receipt. A draft that cannot be parsed records
    /// nothing, so its completed claims are kept; with no draft at all (<paramref name="snapshotJson"/> null), every
    /// receipt counts as recorded.
    /// </summary>
    /// <param name="snapshotJson">The draft's stored snapshot JSON, or null when there is none.</param>
    internal static Func<string, bool> RecordedIn(string? snapshotJson)
    {
        if (snapshotJson is null)
            return _ => true;
        string? receipt = null;
        try
        {
            if (JsonNode.Parse(snapshotJson)?["context"]?["receipt"] is JsonValue value)
                value.TryGetValue(out receipt);
        }
        catch (System.Text.Json.JsonException)
        {
            // Unreadable: nothing is recorded.
        }
        return candidate =>
            receipt is not null && string.Equals(candidate, receipt, StringComparison.Ordinal);
    }

    /// <summary>
    /// Over the Trax store, which records an effect pending on its row, whether a claim the earlier ledger recorded
    /// before the upgrade keeps a draft from being deleted: one in flight within its lease, or a completed one whose
    /// draft is still in its effect's state (<see cref="EffectKey.From"/>; an unknown one counts as that), whose receipt
    /// the next send records.
    /// </summary>
    /// <param name="claims">The ledger's claims under the draft's keys.</param>
    /// <param name="keys">The draft's effect keys, each with the state its effect runs from.</param>
    /// <param name="state">The state the draft is in, or null when it cannot be read.</param>
    internal static bool LedgerHolds(
        IReadOnlyList<LedgerClaim> claims,
        IReadOnlyList<EffectKey> keys,
        string? state
    ) =>
        claims.Any(claim =>
            claim.Receipt is null
                ? claim.LeaseLive
                : keys.Any(k =>
                    k.Key == claim.Key && (k.From is null || state is null || k.From == state)
                )
        );

    /// <summary><see cref="RecordedIn"/> over a stored context column rather than a whole snapshot.</summary>
    /// <param name="contextJson">The draft's context JSON.</param>
    internal static Func<string, bool> RecordedInContext(string contextJson)
    {
        try
        {
            return RecordedIn(
                new JsonObject { ["context"] = JsonNode.Parse(contextJson) }.ToJsonString()
            );
        }
        catch (System.Text.Json.JsonException)
        {
            return _ => false;
        }
    }
}
