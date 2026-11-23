using Microsoft.EntityFrameworkCore.Storage;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;

namespace Trax.Api.Tests.Fakes;

/// <summary>
/// An in-memory data context holding one manifest per external id, for tests that drive the
/// manifest mutations with a substituted scheduler: those mutations look the id up first.
/// </summary>
internal static class SeededManifests
{
    public static async Task<IDataContextProviderFactory> WithAsync(params string[] externalIds)
    {
        var factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        var db = await factory.CreateDbContextAsync(default).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            foreach (var externalId in externalIds)
            {
                var manifest = Manifest.Create(new CreateManifest { Name = typeof(object) });
                manifest.ExternalId = externalId;
                await db.Track(manifest).ConfigureAwait(false);
            }
            await db.SaveChanges(default).ConfigureAwait(false);
        }
        return factory;
    }
}
