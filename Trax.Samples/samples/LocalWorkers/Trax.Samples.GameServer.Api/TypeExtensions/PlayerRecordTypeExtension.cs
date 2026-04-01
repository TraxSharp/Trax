using HotChocolate;
using HotChocolate.Types;
using Trax.Samples.GameServer.Data.Models;

namespace Trax.Samples.GameServer.Api.TypeExtensions;

/// <summary>
/// Extends the <see cref="PlayerRecord"/> GraphQL type with a computed <c>winRate</c>
/// field. Demonstrates how <c>[ExtendObjectType]</c> classes can be auto-registered
/// via <c>AddTypeExtensions(assembly)</c> on the <c>TraxGraphQLBuilder</c>.
/// </summary>
[ExtendObjectType(typeof(PlayerRecord))]
public class PlayerRecordTypeExtension
{
    public double GetWinRate([Parent] PlayerRecord player)
    {
        var total = player.Wins + player.Losses;
        return total > 0 ? (double)player.Wins / total : 0;
    }
}
