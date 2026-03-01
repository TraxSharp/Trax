using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.InMemory.Services.InMemoryContext;

/// <summary>
/// Provides an in-memory implementation of the DataContext for the Trax.Effect.Data system.
/// This class is designed for testing and development scenarios where persistence is not required.
/// </summary>
/// <param name="options">The options to be used by the DbContext</param>
/// <remarks>
/// The InMemoryContext class is a lightweight implementation of the DataContext that uses
/// Entity Framework Core's in-memory database provider. It inherits all functionality from
/// the base DataContext class and requires no additional configuration.
///
/// This implementation is particularly useful for:
/// 1. Unit and integration testing
/// 2. Development and debugging
/// 3. Scenarios where persistence beyond the application lifecycle is not required
///
/// The in-memory database provides a fast, transient storage solution that behaves
/// similarly to a real database but without the overhead of database setup or persistence.
/// This makes it ideal for testing and development scenarios where you want to focus on
/// business logic rather than database interactions.
///
/// Example usage:
/// ```csharp
/// services.AddTraxEffects(options => options.AddInMemoryEffect());
/// ```
///
/// Note that data stored in the in-memory database is lost when the application stops,
/// so this implementation is not suitable for production scenarios where data persistence
/// is required.
/// </remarks>
public class InMemoryContext(DbContextOptions<InMemoryContext> options)
    : DataContext<InMemoryContext>(options) { }
