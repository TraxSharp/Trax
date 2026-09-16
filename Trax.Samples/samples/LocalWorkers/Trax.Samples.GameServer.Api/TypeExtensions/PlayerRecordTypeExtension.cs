using HotChocolate;
using HotChocolate.Types;
using Trax.Effect.Attributes;
using Trax.Samples.GameServer.Data.Models;

namespace Trax.Samples.GameServer.Api.TypeExtensions;

/// <summary>
/// Extends the <see cref="PlayerRecord"/> GraphQL type with a computed <c>winRate</c>
/// field. Demonstrates how <c>[ExtendObjectType]</c> classes can be auto-registered
/// via <c>AddTypeExtensions(assembly)</c> on the <c>TraxGraphQLBuilder</c>.
/// </summary>
/// <remarks>
/// <c>PlayerRecord</c> is <c>[TraxAllowAnonymous]</c>, so a field bolted onto it inherits no
/// gate and has to declare one. Without the attribute below the host refuses to start: a
/// projection off a public entity is not automatically as public as the entity, and Trax makes
/// somebody say which it is. Here the answer is genuinely public, because the field is computed
/// from <c>Wins</c> and <c>Losses</c>, which the same anonymous caller can already read.
/// <para>
/// Use Trax's own attributes here: <c>[TraxAuthorize]</c> and
/// <c>[TraxAllowAnonymous]</c> both apply to a method, and Trax emits the matching
/// <c>@authorize</c> directive. HotChocolate's <c>[Authorize]</c> and <c>[AllowAnonymous]</c> are
/// refused, so a surface declares its posture the same way wherever it lives.
/// </para>
/// </remarks>
[ExtendObjectType(typeof(PlayerRecord))]
public class PlayerRecordTypeExtension
{
    [TraxAllowAnonymous]
    public double GetWinRate(
        // Projection only selects what the caller asked for, and this reads two columns that
        // are not the entity key, so it declares them. Trax adds the key automatically; every
        // other property a resolver touches has to be named or it arrives as 0.
        [Parent(
            requires: nameof(PlayerRecord.Wins) + " " + nameof(PlayerRecord.Losses)
        )] PlayerRecord player
    )
    {
        var total = player.Wins + player.Losses;
        return total > 0 ? (double)player.Wins / total : 0;
    }
}
