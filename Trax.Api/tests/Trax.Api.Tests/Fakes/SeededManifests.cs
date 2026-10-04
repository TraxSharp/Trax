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
    public static IDataContextProviderFactory With(params string[] externalIds)
    {
        var factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        using var db = factory.CreateDbContextAsync(default).GetAwaiter().GetResult();
        foreach (var externalId in externalIds)
        {
            var manifest = Manifest.Create(new CreateManifest { Name = typeof(object) });
            manifest.ExternalId = externalId;
            db.Track(manifest).GetAwaiter().GetResult();
        }
        db.SaveChanges(default).GetAwaiter().GetResult();
        return factory;
    }
}
