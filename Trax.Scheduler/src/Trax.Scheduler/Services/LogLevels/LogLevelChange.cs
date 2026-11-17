using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.LogLevels;

/// <summary>A level to set for a category.</summary>
/// <param name="Category">The category, as configured under <c>Logging:LogLevel</c>; case is ignored.</param>
/// <param name="Level">The level to filter it at.</param>
public record LogLevelChange(string Category, LogLevel Level);
