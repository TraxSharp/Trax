using System.Threading;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Data.Postgres.Services.PostgresContextFactory;

/// <summary>
/// Provides a factory for creating PostgreSQL database contexts for the Trax.Effect.Data system.
/// This factory creates production-ready database contexts for scenarios where persistent storage is required.
/// </summary>
/// <param name="dbContextFactory">The Entity Framework Core DbContext factory to use for creating contexts</param>
/// <remarks>
/// The PostgresContextProviderFactory class is a key component in the Trax.Effect.Data.Postgres system.
/// It implements the IDataContextProviderFactory interface to create PostgresContext instances
/// that use Entity Framework Core's PostgreSQL database provider.
///
/// This factory:
/// 1. Uses Entity Framework Core's DbContextFactory for efficient context creation
/// 2. Tracks the number of contexts created for monitoring purposes
/// 3. Provides both synchronous and asynchronous context creation methods
///
/// Unlike the in-memory implementation, this factory requires a properly configured DbContextFactory
/// that is typically set up with a connection string and other PostgreSQL-specific options.
///
/// The factory is registered with the dependency injection container using
/// the AddPostgresEffect extension method in ServiceExtensions.
///
/// Example usage:
/// ```csharp
/// services.AddTraxEffects(options =>
///     options.AddPostgresEffect("Host=localhost;Database=trax;Username=postgres;Password=password")
/// );
/// ```
/// </remarks>
public class PostgresContextProviderFactory(
    IDbContextFactory<PostgresContext.PostgresContext> dbContextFactory
) : IDataContextProviderFactory
{
    /// <summary>
    /// Gets the number of database contexts created by this factory.
    /// </summary>
    /// <remarks>
    /// This property tracks the total number of database contexts created by this factory instance.
    /// It can be useful for monitoring and debugging purposes, such as detecting context leaks
    /// or understanding the context creation patterns in the application.
    /// </remarks>
    private int _count;

    /// <summary>
    /// Gets the number of database contexts created by this factory.
    /// </summary>
    /// <remarks>
    /// This property tracks the total number of database contexts created by this factory instance.
    /// It can be useful for monitoring and debugging purposes, such as detecting context leaks
    /// or understanding the context creation patterns in the application.
    /// </remarks>
    public int Count => _count;

    /// <summary>
    /// Creates a new PostgreSQL database context.
    /// </summary>
    /// <returns>A new PostgreSQL database context as an IEffectProvider</returns>
    /// <remarks>
    /// This method creates a new PostgresContext instance using the DbContextFactory.
    /// It increments the Count property to track the number of contexts created.
    ///
    /// This method is called by the EffectRunner when it needs to create a new effect provider
    /// for tracking train metadata.
    ///
    /// The context is created with the configuration provided to the DbContextFactory,
    /// which typically includes the connection string and other PostgreSQL-specific options.
    /// </remarks>
    public IEffectProvider Create()
    {
        var context = dbContextFactory.CreateDbContext();

        Interlocked.Increment(ref _count);

        return context;
    }

    /// <summary>
    /// Creates a new PostgreSQL database context asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests</param>
    /// <returns>A task that resolves to a new PostgreSQL database context</returns>
    /// <remarks>
    /// This method creates a new PostgresContext instance asynchronously using the DbContextFactory.
    /// It increments the Count property to track the number of contexts created.
    ///
    /// This method is typically called when a new database context is needed for
    /// operations that require direct database access outside of the EffectRunner flow.
    ///
    /// The asynchronous creation is particularly useful in web applications and other
    /// scenarios where non-blocking operations are important for scalability.
    /// </remarks>
    public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        Interlocked.Increment(ref _count);

        return context;
    }
}
