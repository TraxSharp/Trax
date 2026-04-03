using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteContextTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    #region OnModelCreating

    [Test]
    public void SqliteContext_OnModelCreating_StripsSchemaFromAllEntities()
    {
        var context = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var dbContext = (DbContext)context;
        var model = dbContext.Model;

        foreach (var entityType in model.GetEntityTypes())
        {
            entityType
                .GetSchema()
                .Should()
                .BeNull($"entity {entityType.Name} should have no schema in SQLite");
        }
    }

    [Test]
    public void SqliteContext_OnModelCreating_RemapsJsonbColumnsToText()
    {
        var context = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var dbContext = (DbContext)context;
        var model = dbContext.Model;

        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var columnType = property.GetColumnType();
                columnType
                    .Should()
                    .NotBe(
                        "jsonb",
                        $"property {entityType.Name}.{property.Name} should not use jsonb in SQLite"
                    );
            }
        }
    }

    [Test]
    public void SqliteContext_OnModelCreating_AppliesUtcConverterToAllDateTimeProperties()
    {
        var context = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var dbContext = (DbContext)context;
        var model = dbContext.Model;

        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property
                        .GetValueConverter()
                        .Should()
                        .NotBeNull(
                            $"DateTime property {entityType.Name}.{property.Name} should have a UTC converter"
                        );
                }
            }
        }
    }

    #endregion

    #region Factory and Interface

    [Test]
    public void SqliteContext_CanBeCreatedFromFactory()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = factory.Create();

        context.Should().NotBeNull();
        context.Should().BeAssignableTo<IDataContext>();
    }

    [Test]
    public void SqliteContext_FactoryCreatesDistinctInstances()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var first = factory.Create();
        var second = factory.Create();

        first.Should().NotBeSameAs(second);
    }

    [Test]
    public void SqliteContext_ImplementsIDataContext()
    {
        var context = Scope.ServiceProvider.GetRequiredService<IDataContext>();

        context.Should().BeAssignableTo<IDataContext>();
        context.Should().BeAssignableTo<DbContext>();
    }

    #endregion
}
