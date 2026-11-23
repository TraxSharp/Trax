using Microsoft.Extensions.Logging;

namespace Trax.Api.DTOs;

/// <summary>A level to set for one category with <c>config.setLogLevels</c>.</summary>
/// <param name="Category">The category, as configured under <c>Logging:LogLevel</c>; case is ignored.</param>
/// <param name="Level">The level to filter it at.</param>
public record LogLevelSettingInput(string Category, LogLevel Level);
