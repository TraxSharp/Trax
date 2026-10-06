using System.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="IDataContext.BeginTransaction(IsolationLevel)"/> opens the transaction at the level
/// it is given, read back from Postgres inside it.
/// </summary>
[TestFixture]
public class DataContextIsolationLevelTests : TestSetup
{
    [TestCase(IsolationLevel.Serializable, "serializable")]
    [TestCase(IsolationLevel.RepeatableRead, "repeatable read")]
    [TestCase(IsolationLevel.ReadCommitted, "read committed")]
    public async Task The_transaction_runs_at_the_requested_level(
        IsolationLevel level,
        string expected
    )
    {
        using var context = (IDataContext)DataContextFactory.Create();
        using var transaction = await context.BeginTransaction(level);

        (await CurrentIsolation(context)).Should().Be(expected);
    }

    [Test]
    public async Task The_cancellable_overload_runs_at_the_requested_level()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        using var transaction = await context.BeginTransaction(
            IsolationLevel.Serializable,
            CancellationToken.None
        );

        (await CurrentIsolation(context)).Should().Be("serializable");
    }

    private static Task<string> CurrentIsolation(IDataContext context) =>
        ((DbContext)context)
            .Database.SqlQueryRaw<string>(
                "SELECT current_setting('transaction_isolation') AS \"Value\""
            )
            .SingleAsync();
}
