using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Postgres.Services.PostgresContext;

/// <summary>
/// Provides a PostgreSQL implementation of the DataContext for the Trax.Effect.Data system.
/// This class is designed for production scenarios where persistent storage is required.
/// </summary>
/// <param name="options">The options to be used by the DbContext</param>
/// <remarks>
/// The PostgresContext class is a production-ready implementation of the DataContext that uses
/// Entity Framework Core's PostgreSQL database provider. It inherits the core functionality from
/// the base DataContext class and adds PostgreSQL-specific configurations.
///
/// This implementation is particularly useful for:
/// 1. Production environments
/// 2. Scenarios requiring persistent storage
/// 3. Applications with complex data requirements
///
/// The PostgreSQL database provides a robust, scalable storage solution with advanced features
/// such as JSON support, complex queries, and transactional integrity.
///
/// Key PostgreSQL-specific configurations include:
/// 1. Setting the default schema to "trax"
/// 2. Mapping enum types to PostgreSQL enum types
/// 3. Ensuring UTC date/time handling
///
/// Example usage:
/// ```csharp
/// services.AddTrax(trax => trax
///     .AddEffects(effects => effects.UsePostgres("Host=localhost;Database=trax;Username=postgres;Password=password"))
/// );
/// ```
/// </remarks>
public class PostgresContext(DbContextOptions<PostgresContext> options)
    : DataContext<PostgresContext>(options)
{
    /// <summary>
    /// Configures the model that was discovered by convention from the entity types.
    /// </summary>
    /// <param name="modelBuilder">The builder being used to construct the model for this context</param>
    /// <remarks>
    /// This method overrides the base OnModelCreating method to add PostgreSQL-specific configurations:
    ///
    /// 1. Sets the default schema to "trax" to isolate Trax.Core tables
    /// 2. Adds PostgreSQL enum mappings for train states and log levels
    /// 3. Applies UTC date/time conversion to ensure consistent timestamp handling
    ///
    /// These configurations ensure that the PostgreSQL database is properly set up to work with
    /// the Trax.Effect system, with appropriate data types and schema organization.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("trax").AddPostgresEnums().ApplyUtcDateTimeConverter();
    }
}
