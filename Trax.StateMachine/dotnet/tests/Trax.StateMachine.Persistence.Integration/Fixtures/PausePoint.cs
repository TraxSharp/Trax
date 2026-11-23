using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// Where <see cref="PausingSnapshotStore"/> holds the next write: before it reaches the store (after the caller's
/// read), or after the store has written it and before the caller hears back.
/// </summary>
public enum PausePoint
{
    BeforeWrite,
    AfterWrite,
}
