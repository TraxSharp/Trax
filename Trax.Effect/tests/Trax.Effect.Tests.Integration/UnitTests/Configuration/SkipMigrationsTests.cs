using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

[TestFixture]
public class SkipMigrationsTests
{
    /// <summary>
    /// An unreachable connection string (RFC 5737 TEST-NET-1 address, port 1, minimal timeout).
    /// If migration runs, it will throw a connection error. If skipped, UsePostgres() still
    /// builds the NpgsqlDataSource (which doesn't connect eagerly) and completes successfully.
    /// </summary>
    private const string UnreachableConnectionString =
        "Host=192.0.2.1;Port=1;Database=x;Username=x;Password=x;Timeout=1";

    #region Builder State

    [Test]
    public void MigrationsDisabled_DefaultsFalse()
    {
        var services = new ServiceCollection();
        var migrationsDisabled = false;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                migrationsDisabled = effects.MigrationsDisabled;
                return effects;
            })
        );

        migrationsDisabled.Should().BeFalse();
    }

    [Test]
    public void SkipMigrations_SetsMigrationsDisabledTrue()
    {
        var services = new ServiceCollection();
        var migrationsDisabled = false;

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var result = effects.SkipMigrations();
                migrationsDisabled = result.MigrationsDisabled;
                return result;
            })
        );

        migrationsDisabled.Should().BeTrue();
    }

    [Test]
    public void SkipMigrations_SurvivesPromotionToBuilderWithData()
    {
        // SkipMigrations() is called before UsePostgres(), which promotes the builder to
        // TraxEffectBuilderWithData via the copy constructor. The flag must survive promotion.
        // If migration is NOT skipped, UsePostgres() with an unreachable host would throw.
        var act = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.SkipMigrations().UsePostgres(UnreachableConnectionString)
                )
            );

        act.Should().NotThrow();
    }

    #endregion

    #region Integration with UsePostgres

    [Test]
    public void UsePostgres_WithSkipMigrations_DoesNotAttemptConnection()
    {
        var act = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.SkipMigrations().UsePostgres(UnreachableConnectionString)
                )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void UsePostgres_WithoutSkipMigrations_ThrowsOnUnreachableDb()
    {
        var act = () =>
            new ServiceCollection().AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(UnreachableConnectionString))
            );

        act.Should().Throw<Exception>();
    }

    [Test]
    public void UsePostgres_WithSkipMigrations_StillRegistersDbContextFactory()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.SkipMigrations().UsePostgres(UnreachableConnectionString)
            )
        );

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType.IsGenericType
                && sd.ServiceType.GetGenericTypeDefinition()
                    == typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<>)
            );
    }

    [Test]
    public void UsePostgres_WithSkipMigrations_StillRegistersDataContext()
    {
        var services = new ServiceCollection();

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.SkipMigrations().UsePostgres(UnreachableConnectionString)
            )
        );

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(IDataContext) && sd.Lifetime == ServiceLifetime.Scoped
            );
    }

    #endregion
}
