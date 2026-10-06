using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Trax.Dashboard.Tests.Stress.Fixtures;

/// <summary>
/// Records the text of every command EF Core sends, so a test can say which columns a grid
/// query selects and whether it counted the table.
/// </summary>
public sealed class SqlCapture : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
                return [.. _commands];
        }
    }

    public void Clear()
    {
        lock (_commands)
            _commands.Clear();
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        lock (_commands)
            _commands.Add(command.CommandText);
    }
}
