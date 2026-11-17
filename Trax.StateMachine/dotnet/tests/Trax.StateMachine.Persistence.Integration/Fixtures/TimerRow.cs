using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

/// <summary>A draft row's timer columns as the database holds them, read by raw SQL.</summary>
/// <param name="State">The state column.</param>
/// <param name="DueAt">The <c>due_at</c> column, in UTC.</param>
/// <param name="Revision">The <c>revision</c> column.</param>
public sealed record TimerRow(string State, DateTimeOffset? DueAt, long Revision);
