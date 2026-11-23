using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Shop;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Tickets;

/// <summary>Reads a context the way the machines write it, for code that builds an invoked run's input.</summary>
internal static class ContextReading
{
    public static string String(this JsonObject ctx, string field) =>
        ctx[field]?.GetValue<string>() ?? "";

    public static decimal Number(this JsonObject ctx, string field) =>
        ctx[field]?.GetValue<decimal>() ?? 0m;
}
