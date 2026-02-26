using System.Reflection;
using Trax.Effect.Models;
using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Extensions;

/// <summary>
/// Provides extension methods for configuring Entity Framework Core model builders in the Trax.Effect.Data system.
/// </summary>
/// <remarks>
/// The ModelBuilderExtensions class contains utility methods that simplify the configuration
/// of Entity Framework Core models for the Trax.Effect.Data system.
///
/// These extensions enable:
/// 1. Automatic discovery and application of entity configurations
/// 2. Consistent model configuration across different database contexts
/// 3. Separation of entity configuration from the DbContext implementation
///
/// By using these extensions, the system can maintain a clean separation of concerns
/// while ensuring consistent entity configuration across different database implementations.
/// </remarks>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies entity configurations from all model classes in the assembly.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure</param>
    /// <returns>The configured model builder for method chaining</returns>
    /// <remarks>
    /// This method uses reflection to discover all entity types that implement IModel
    /// and have a static OnModelCreating method. It then invokes that method on each entity type,
    /// passing the model builder as a parameter.
    ///
    /// This approach allows each entity to define its own configuration logic,
    /// which is then automatically applied when the model is being built.
    /// This promotes a clean separation of concerns, where each entity is responsible
    /// for its own configuration, rather than centralizing all configuration in the DbContext.
    ///
    /// The method:
    /// 1. Finds all non-abstract classes that implement IModel
    /// 2. Looks for a static OnModelCreating method on each class
    /// 3. Verifies that the method has the correct signature
    /// 4. Invokes the method, passing the model builder
    ///
    /// If an entity type doesn't have a valid OnModelCreating method,
    /// an exception is thrown to ensure that all entities are properly configured.
    /// </remarks>
    public static ModelBuilder ApplyEntityOnModelCreating(this ModelBuilder modelBuilder)
    {
        var entityTypes = typeof(AssemblyMarker)
            .Assembly.GetTypes()
            .Where(
                t => typeof(IModel).IsAssignableFrom(t) && t is { IsAbstract: false, IsClass: true }
            )
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var method =
                entityType.GetMethod(
                    "OnModelCreating",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
                ) ?? throw new Exception($"Could not find OnModelCreating on {entityType.Name}");

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(ModelBuilder))
                throw new Exception($"{entityType.Name}.OnModelCreating is the wrong signature");

            method.Invoke(null, [modelBuilder]);
        }

        return modelBuilder;
    }
}
