using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Two <see cref="E2EHost"/>s over one throwaway database, as two nodes of one deployment: a write on one node is
/// a write the other did not make. Each node's sessions carry their own <c>application_name</c>, so a test can cut
/// one node off the database with <see cref="TerminateAsync"/> and watch it reconnect, as
/// <c>DispatcherWakeTests</c> does with its listener.
/// </summary>
/// <remarks>
/// The nodes start one after the other, so only the first runs the migrations against an empty database.
/// </remarks>
public sealed class TwoNodes : IAsyncDisposable
{
    private TwoNodes(string database, IHost a, IHost b, string nameA, string nameB)
    {
        Database = database;
        A = a;
        B = b;
        ApplicationNameA = nameA;
        ApplicationNameB = nameB;
    }

    /// <summary>The database both nodes use.</summary>
    public string Database { get; }

    /// <summary>The first node.</summary>
    public IHost A { get; }

    /// <summary>The second node.</summary>
    public IHost B { get; }

    /// <summary>The <c>application_name</c> of <see cref="A"/>'s sessions.</summary>
    public string ApplicationNameA { get; }

    /// <summary>The <c>application_name</c> of <see cref="B"/>'s sessions.</summary>
    public string ApplicationNameB { get; }

    /// <summary>
    /// Recreates <paramref name="database"/> and starts two nodes on it with the same <paramref name="options"/>
    /// and the same <paramref name="charge"/>, so the effect's delivery count covers both.
    /// </summary>
    public static async Task<TwoNodes> StartAsync(
        string database,
        IOrderCharge charge,
        E2EHostOptions? options = null
    )
    {
        options ??= new E2EHostOptions();
        var nameA = $"{database}_node_a";
        var nameB = $"{database}_node_b";

        await E2EHost.RecreateDatabaseAsync(database);
        var a = await E2EHost.StartAsync(
            database,
            charge,
            options with
            {
                ApplicationName = nameA,
            }
        );
        try
        {
            var b = await E2EHost.StartAsync(
                database,
                charge,
                options with
                {
                    ApplicationName = nameB,
                }
            );
            return new TwoNodes(database, a, b, nameA, nameB);
        }
        catch
        {
            a.Dispose();
            throw;
        }
    }

    /// <summary>The <c>application_name</c> of <paramref name="node"/>'s sessions.</summary>
    public string ApplicationNameOf(IHost node) =>
        ReferenceEquals(node, A) ? ApplicationNameA
        : ReferenceEquals(node, B) ? ApplicationNameB
        : throw new ArgumentException("Not one of these two nodes.", nameof(node));

    /// <summary>
    /// Terminates every database session <paramref name="node"/> holds, its listener included, and returns how
    /// many there were. A command already holding one of those sessions fails (the first one after this can too);
    /// later commands open fresh connections, and the node's listener reconnects on its own backoff.
    /// </summary>
    public async Task<long> TerminateAsync(IHost node)
    {
        await using var admin = new NpgsqlConnection(E2EHost.ConnectionString(Database));
        await admin.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity "
                + "WHERE application_name = @name AND datname = current_database()",
            admin
        );
        terminate.Parameters.AddWithValue("name", ApplicationNameOf(node));
        var terminated = (long)(await terminate.ExecuteScalarAsync())!;

        // The pool still holds the dead connections; the next command must open a fresh one.
        NpgsqlConnection.ClearAllPools();
        return terminated;
    }

    /// <summary>Stops both nodes and drops the database.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var node in new[] { A, B })
        {
            try
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await node.StopAsync(stop.Token);
            }
            catch (OperationCanceledException) { }
            node.Dispose();
        }
        await E2EHost.DropDatabaseAsync(Database);
    }
}
