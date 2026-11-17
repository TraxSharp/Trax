using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.StateMachine.Persistence;

/// <summary>The most drafts one user may hold, and the idle time after which one no longer counts.</summary>
/// <param name="Max">The cap.</param>
/// <param name="Ttl">The draft TTL, or null when drafts never expire.</param>
/// <param name="Kept">
/// For a row that does not record whether the server put it in its state, the states that make it a record rather than
/// a draft, as <see cref="DraftExpiry.Key"/> writes them; null names none.
/// </param>
internal readonly record struct DraftCap(
    int Max,
    TimeSpan? Ttl,
    IReadOnlyCollection<string>? Kept = null
);
