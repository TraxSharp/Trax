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

/// <summary>How a create through <see cref="InvokeOutbox.Create"/> ended.</summary>
internal abstract record CreateWrite
{
    /// <summary>The row was inserted, with its run when its state invokes one.</summary>
    public sealed record Created(Guid Id, WrittenRow Row) : CreateWrite;

    /// <summary>The user's start with the same request id and input created this row earlier.</summary>
    public sealed record Existing(Guid Id, StoredSnapshot Stored) : CreateWrite;

    /// <summary>The user's start with the same request id was started with a different input.</summary>
    public sealed record Reused : CreateWrite;

    /// <summary>The row already exists (a save racing another create of the same id); nothing was written.</summary>
    public sealed record Conflict : CreateWrite;

    /// <summary>The create was refused; nothing was written.</summary>
    public sealed record Refused(RefusalCode Code, string Message, Exception? Exception = null)
        : CreateWrite;

    private CreateWrite() { }
}
