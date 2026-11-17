using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.StateMachine.Persistence.Integration.Fakes;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

/// <summary>The two providers that store drafts in a real database.</summary>
public enum StoreProvider
{
    Postgres,
    Sqlite,
}
