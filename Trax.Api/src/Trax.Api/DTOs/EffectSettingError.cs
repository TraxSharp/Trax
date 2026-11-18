using Trax.Scheduler.Services.Effects;

namespace Trax.Api.DTOs;

/// <summary>A setting <c>operations.configureEffect</c> refused, and why.</summary>
/// <param name="Field">The setting's name, as it was given.</param>
/// <param name="Message">Why it was refused.</param>
public record EffectSettingError(string Field, string Message);
