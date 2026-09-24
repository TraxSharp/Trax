using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;

namespace Trax.Dashboard.Tests.Integration.Fakes.Data;

/// <summary>
/// The Trax data context over EF Core's in-memory provider, so a page that reads and writes
/// through <see cref="IDataContextProviderFactory"/> can be rendered without a database. Every
/// context it creates shares one store, as the in-memory provider in Trax.Effect.Data.InMemory
/// does; that package is not referenced here.
/// </summary>
public sealed class InMemoryDataContextFactory : IDataContextProviderFactory
{
    private readonly InMemoryDatabaseRoot _root = new();

    public IDataContext Create() =>
        new InMemoryDataContext(
            new DbContextOptionsBuilder<InMemoryDataContext>()
                .UseInMemoryDatabase("dashboard-tests", _root)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options
        );

    public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Create());

    IEffectProvider IEffectProviderFactory.Create() => Create();
}

public sealed class InMemoryDataContext(DbContextOptions<InMemoryDataContext> options)
    : DataContext<InMemoryDataContext>(options);
