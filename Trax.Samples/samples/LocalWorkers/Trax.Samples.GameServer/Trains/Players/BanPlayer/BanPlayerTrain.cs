using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Players.BanPlayer.Junctions;

namespace Trax.Samples.GameServer.Trains.Players.BanPlayer;

/// <summary>
/// Admin-only train for banning players.
/// Requires the "Admin" authorization policy — players with only the "Player" role get 403.
/// Demonstrates per-train lifecycle hook overrides alongside the global AuditLogHook.
/// </summary>
[TraxAuthorize("Admin")]
[TraxMutation(
    GraphQLOperation.Run,
    Namespace = GraphQLNamespaces.Players,
    Description = "Bans a player (admin only)"
)]
public class BanPlayerTrain(ILogger<BanPlayerTrain> logger)
    : ServiceTrain<BanPlayerInput, Unit>,
        IBanPlayerTrain
{
    protected override Unit Junctions() => Chain<ApplyBanJunction>();

    protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        logger.LogInformation(
            "Player {PlayerId} banned for: {Reason} (Train: {TrainName})",
            TrainInput?.PlayerId,
            TrainInput?.Reason,
            metadata.Name
        );
        return Task.CompletedTask;
    }

    protected override Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        logger.LogWarning(
            "Failed to ban player {PlayerId} (Train: {TrainName}): {Message}",
            TrainInput?.PlayerId,
            metadata.Name,
            exception.Message
        );
        return Task.CompletedTask;
    }
}
