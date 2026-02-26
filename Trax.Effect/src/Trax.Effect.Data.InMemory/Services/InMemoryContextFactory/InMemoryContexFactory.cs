using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;

/// <summary>
/// Provides a factory for creating in-memory database contexts for the Trax.Effect.Data system.
/// This factory creates lightweight, transient database contexts for testing and development scenarios.
/// </summary>
/// <remarks>
/// The InMemoryContextProviderFactory class is a key component in the Trax.Effect.Data.InMemory system.
/// It implements the IDataContextProviderFactory interface to create InMemoryContext instances
/// that use Entity Framework Core's in-memory database provider.
///
/// This factory:
/// 1. Creates InMemoryContext instances with a default in-memory database name
/// 2. Requires no external configuration or dependencies
/// 3. Provides a simple implementation suitable for testing and development
///
/// Unlike production database factories, this implementation doesn't require connection strings
/// or complex configuration, making it ideal for unit tests and development environments
/// where database setup should be minimal.
///
/// The factory is typically registered with the dependency injection container using
/// the AddInMemoryEffect extension method in ServiceExtensions.
///
/// Example usage:
/// ```csharp
/// services.AddTrax.CoreEffects(options => options.AddInMemoryEffect());
/// ```
/// </remarks>
public class InMemoryContextProviderFactory : IDataContextProviderFactory
{
    /// <summary>
    /// Creates a new in-memory database context.
    /// </summary>
    /// <returns>A new in-memory database context</returns>
    /// <remarks>
    /// This method creates a new InMemoryContext instance configured to use
    /// Entity Framework Core's in-memory database provider with a default
    /// database name ("InMemoryDb").
    ///
    /// The context is created with minimal configuration, making it suitable
    /// for testing and development scenarios where database setup should be simple.
    ///
    /// This method is called by the EffectRunner when it needs to create
    /// a new effect provider for tracking workflow metadata.
    /// </remarks>
    public IDataContext Create() =>
        new InMemoryContext.InMemoryContext(
            new DbContextOptionsBuilder<InMemoryContext.InMemoryContext>()
                .UseInMemoryDatabase("InMemoryDb")
                .Options
        );

    /// <summary>
    /// Creates a new in-memory database context asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests</param>
    /// <returns>A task that resolves to a new in-memory database context</returns>
    /// <remarks>
    /// This method provides an asynchronous version of the Create method to satisfy
    /// the IDataContextProviderFactory interface. Since creating an in-memory context
    /// is a synchronous operation, this method simply delegates to the Create method.
    ///
    /// This method is typically called when a new database context is needed for
    /// operations that require direct database access outside of the EffectRunner flow.
    /// </remarks>
    public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        return Create();
    }

    /// <summary>
    /// Explicit implementation of IEffectProviderFactory.Create that delegates to the Create method.
    /// </summary>
    /// <returns>A new in-memory database context as an IEffectProvider</returns>
    /// <remarks>
    /// This explicit interface implementation satisfies the IEffectProviderFactory interface
    /// by delegating to the Create method. This ensures that the same context creation logic
    /// is used regardless of whether the factory is accessed through IDataContextProviderFactory
    /// or IEffectProviderFactory.
    ///
    /// This method is called by the EffectRunner when it needs to create a new effect provider
    /// for tracking workflow metadata.
    /// </remarks>
    IEffectProvider IEffectProviderFactory.Create() => Create();
}
