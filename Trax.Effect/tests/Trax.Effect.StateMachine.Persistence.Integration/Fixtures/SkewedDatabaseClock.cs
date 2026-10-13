using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// Puts the database's clock an hour ahead of this host's, as seen through the host's data contexts: every command
/// that reads the clock (<c>clock_timestamp()</c> and <c>now()</c> on Postgres, <c>'now'</c> on SQLite) reads it an
/// hour later. A value armed from the database's clock then lands an hour from one armed from the host's.
/// </summary>
internal sealed class SkewedDatabaseClock : DbCommandInterceptor
{
    public static readonly TimeSpan Ahead = TimeSpan.FromHours(1);

    /// <summary>Adds the skew to every data context the host's container builds.</summary>
    public static void Install(IServiceCollection services)
    {
        var context = services
            .Select(d => d.ServiceType)
            .First(t =>
                t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDbContextFactory<>)
            )
            .GetGenericArguments()[0];
        var configure = typeof(EntityFrameworkServiceCollectionExtensions)
            .GetMethods()
            .Single(m =>
                m.Name == nameof(EntityFrameworkServiceCollectionExtensions.ConfigureDbContext)
                && m.GetParameters() is [_, { ParameterType: var action }, _]
                && action == typeof(Action<IServiceProvider, DbContextOptionsBuilder>)
            )
            .MakeGenericMethod(context);
        configure.Invoke(
            null,
            [
                services,
                (Action<IServiceProvider, DbContextOptionsBuilder>)(
                    (_, options) => options.AddInterceptors(new SkewedDatabaseClock())
                ),
                ServiceLifetime.Scoped,
            ]
        );
    }

    private static void Skew(DbCommand command) =>
        command.CommandText = command
            .CommandText.Replace("clock_timestamp()", "(clock_timestamp() + interval '1 hour')")
            .Replace("now()", "(now() + interval '1 hour')")
            .Replace("'now')", "'now', '+1 hours')");

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Skew(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Skew(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Skew(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Skew(command);
        return ValueTask.FromResult(result);
    }
}
