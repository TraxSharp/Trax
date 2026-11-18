using Npgsql;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.Api.StateMachine.E2E;

/// <summary>One <c>snapshot_draft</c> row, as <see cref="E2ESql.DraftAsync"/> reads it.</summary>
public sealed record DraftRow(
    string State,
    string Context,
    Guid ConcurrencyToken,
    DateTimeOffset UpdatedAt,
    string UserKey
);
