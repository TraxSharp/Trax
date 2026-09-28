using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Models.RunnerNonce;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>trax.runner_nonce</c> through <see cref="IDataContext.RunnerNonces"/> on a database built by the
/// shipped migrations: the model round-trips, the expiry compares and updates as a number of seconds,
/// and a second insert of one nonce is a conflict <see cref="ISqlDialect.IsUniqueViolation"/>
/// recognises, while an unrelated constraint failure is not. These are the pieces Trax.Scheduler's
/// database nonce store is built from.
/// </summary>
/// <remarks>
/// Every test uses nonces of its own and deletes them, so nothing it writes is seen by another suite
/// sharing the database.
/// </remarks>
[TestFixture]
public class RunnerNonceStorageTests : TestSetup
{
    private readonly List<string> _nonces = [];

    private ISqlDialect Dialect => Scope.ServiceProvider.GetRequiredService<ISqlDialect>();

    [TearDown]
    public async Task DeleteNonces()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        await context.RunnerNonces.Where(n => _nonces.Contains(n.Nonce)).ExecuteDeleteAsync();
        _nonces.Clear();
    }

    private string NewNonce()
    {
        var nonce = $"nonce-{Guid.NewGuid():N}";
        _nonces.Add(nonce);
        return nonce;
    }

    private async Task Insert(string nonce, DateTimeOffset expiresAt)
    {
        using var context = (IDataContext)DataContextFactory.Create();
        context.RunnerNonces.Add(new RunnerNonce { Nonce = nonce, ExpiresAt = expiresAt });
        await context.SaveChanges(CancellationToken.None);
    }

    [Test]
    public async Task A_nonce_round_trips_to_the_second()
    {
        var nonce = NewNonce();
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(1_900_000_000).AddMilliseconds(750);

        await Insert(nonce, expiresAt);

        using var context = (IDataContext)DataContextFactory.Create();
        var stored = await context.RunnerNonces.AsNoTracking().SingleAsync(n => n.Nonce == nonce);
        stored
            .ExpiresAt.Should()
            .Be(
                DateTimeOffset.FromUnixTimeSeconds(1_900_000_000),
                "expires_at is Unix seconds, so the fraction of a second is dropped"
            );
    }

    [Test]
    public async Task Expiry_compares_updates_and_deletes_as_seconds()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = NewNonce();
        var live = NewNonce();
        await Insert(expired, now.AddMinutes(-1));
        await Insert(live, now.AddMinutes(5));

        using var context = (IDataContext)DataContextFactory.Create();
        var mine = context.RunnerNonces.Where(n => _nonces.Contains(n.Nonce));

        (await mine.Where(n => n.ExpiresAt < now).Select(n => n.Nonce).ToListAsync())
            .Should()
            .Equal(expired);

        var later = now.AddMinutes(10);
        (
            await mine.Where(n => n.Nonce == expired && n.ExpiresAt < now)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ExpiresAt, later))
        )
            .Should()
            .Be(1, "an expired row can be taken over");
        (
            await mine.Where(n => n.Nonce == live && n.ExpiresAt < now)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ExpiresAt, later))
        )
            .Should()
            .Be(0, "a live row is left alone");

        (await mine.Where(n => n.ExpiresAt < later).ExecuteDeleteAsync()).Should().Be(1);
        (await mine.Select(n => n.Nonce).ToListAsync()).Should().Equal(expired);
    }

    [Test]
    public async Task A_second_insert_of_one_nonce_is_a_unique_violation()
    {
        var nonce = NewNonce();
        await Insert(nonce, DateTimeOffset.UtcNow.AddMinutes(5));

        var act = () => Insert(nonce, DateTimeOffset.UtcNow.AddMinutes(5));

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.Should()
            .Match<DbUpdateException>(
                e => Dialect.IsUniqueViolation(e),
                "the primary key refused a nonce that is already recorded"
            );
    }

    [Test]
    public async Task Two_concurrent_inserts_of_one_nonce_are_one_row_and_one_unique_violation()
    {
        var nonce = NewNonce();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Insert(nonce, expiresAt);
                            return "inserted";
                        }
                        catch (DbUpdateException e) when (Dialect.IsUniqueViolation(e))
                        {
                            return "conflict";
                        }
                    })
                )
        );

        outcomes.Count(o => o == "inserted").Should().Be(1);
        outcomes.Count(o => o == "conflict").Should().Be(7);
    }

    [Test]
    public async Task Another_constraint_failure_is_not_a_unique_violation()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        // scheduler_config is a singleton: CHECK (id = 1). Id 2 fails that check, not a key.
        context.SchedulerConfigs.Add(new SchedulerConfig { Id = 2 });

        var act = () => context.SaveChanges(CancellationToken.None);

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.Should()
            .Match<DbUpdateException>(
                e => !Dialect.IsUniqueViolation(e),
                "a failure that is not a conflict must throw, not read as one"
            );
    }
}
