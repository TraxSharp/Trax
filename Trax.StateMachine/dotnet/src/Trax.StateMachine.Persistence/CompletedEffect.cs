using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using EffectClaim = Trax.Effect.Models.EffectClaim.EffectClaim;

namespace Trax.StateMachine.Persistence;

/// <summary>A claim whose effect has run: its receipt, and the fingerprint of the content the effect ran on.</summary>
/// <param name="Receipt">The effect's recorded result.</param>
/// <param name="ContentFingerprint">
/// The fingerprint the claim was taken with (see <see cref="SnapshotFingerprint"/>), or null for a claim taken
/// without one: recorded before fingerprints existed, or by a store that does not keep them. A null fingerprint
/// binds the receipt to no particular content.
/// </param>
public sealed record CompletedEffect(string Receipt, string? ContentFingerprint);
