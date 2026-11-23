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

/// <summary>How a write through the <see cref="InvokeOutbox"/> ended.</summary>
internal abstract record InvokeWrite
{
    /// <summary>The snapshot, the token change and any queued run committed together.</summary>
    /// <param name="InvokeToken">The token the row holds now: the new run's external id, or null.</param>
    public sealed record Written(string? InvokeToken) : InvokeWrite
    {
        /// <summary>
        /// The due time the write left on the row, where the writer reads it: when the timer of its state fires, or
        /// null when that state has none.
        /// </summary>
        public DateTimeOffset? DueAt { get; init; }
    }

    /// <summary>The row changed since it was read, or (on insert) already exists; nothing was written.</summary>
    public sealed record Conflict : InvokeWrite;

    /// <summary>The write was refused; nothing was written.</summary>
    public sealed record Refused(RefusalCode Code, string Message, Exception? Exception = null)
        : InvokeWrite;

    private InvokeWrite() { }
}
