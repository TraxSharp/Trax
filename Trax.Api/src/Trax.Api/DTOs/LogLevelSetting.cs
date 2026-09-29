namespace Trax.Api.DTOs;

/// <summary>
/// One entry of the host's <c>Logging:LogLevel</c> configuration section.
/// </summary>
/// <param name="Category">The logging category, <c>Default</c> for the fallback.</param>
/// <param name="Level">The minimum level configured for it, as written in configuration.</param>
public record LogLevelSetting(string Category, string Level);
