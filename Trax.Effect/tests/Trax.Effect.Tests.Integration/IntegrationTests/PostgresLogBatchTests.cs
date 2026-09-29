using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextLoggingProvider;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// One log entry Postgres cannot store must not take the rest of its flush batch with it.
/// </summary>
/// <remarks>
/// The provider's flush loop creates its context before it reads a single entry, so a factory
/// that holds that creation until every entry is written makes the whole set one batch.
/// </remarks>
[TestFixture]
public class PostgresLogBatchTests : TestSetup
{
    private const string Category = "Audit.PostgresLogBatchTests";

    [Test]
    public async Task A_log_message_holding_a_NUL_does_not_drop_the_other_entries_in_its_batch()
    {
        var stored = await LogOneBatch(bad: "user name: admin\0x");

        stored
            .Should()
            .BeGreaterThanOrEqualTo(
                10,
                "ten ordinary entries were written alongside the one Postgres refuses"
            );
    }

    [Test]
    public async Task A_log_message_truncated_through_a_surrogate_pair_is_still_stored()
    {
        // Log.Create truncates a message to 4000 UTF-16 units. An emoji straddling that boundary
        // is cut in half, and Postgres cannot encode half a character.
        var bad = new string('a', 3999) + "\U0001F600" + "tail";

        var stored = await LogOneBatch(bad);

        stored.Should().Be(11, "every entry, the long one included, is storable once truncated");
    }

    [Test]
    public async Task An_entry_Postgres_still_refuses_costs_only_its_own_line()
    {
        // A lone surrogate the caller logged, not one Trax's truncation made: the driver cannot
        // encode it, so the batch fails and its entries are stored one at a time.
        var stored = await LogOneBatch(bad: "half a character: \uD83D here");

        stored.Should().Be(10, "only the entry the database refuses is lost");
    }

    private async Task<int> LogOneBatch(string bad)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new DataContextLoggingProvider(
            new GatedFactory(DataContextFactory, gate.Task),
            new Config()
        );

        var logger = provider.CreateLogger(Category);
        for (var i = 0; i < 5; i++)
            logger.LogWarning("ordinary entry {Index}", i);
        logger.LogWarning("{Value}", bad);
        for (var i = 5; i < 10; i++)
            logger.LogWarning("ordinary entry {Index}", i);

        gate.SetResult();

        // Polled rather than waited on: the loop flushes on its own schedule. A batch Postgres
        // refuses never lands, so the count stays at zero until the deadline.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var stored = 0;
        while (DateTime.UtcNow < deadline && (stored = await CountStored()) < 10)
            await Task.Yield();

        provider.Dispose();
        return await CountStored();
    }

    private async Task<int> CountStored()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        return await context.Logs.AsNoTracking().CountAsync(l => l.Category == Category);
    }

    private sealed class Config : IDataContextLoggingProviderConfiguration
    {
        public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;
        public List<string> Blacklist { get; init; } = [];
    }

    private sealed class GatedFactory(IDataContextProviderFactory inner, Task gate)
        : IDataContextProviderFactory
    {
        public IEffectProvider Create() => inner.Create();

        public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return await inner.CreateDbContextAsync(cancellationToken);
        }
    }
}
