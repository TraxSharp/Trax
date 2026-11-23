using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Services.Effects;

/// <summary>What <see cref="IEffectSettingsService.ConfigureEffect"/> did.</summary>
/// <param name="Success">Whether the settings were written.</param>
/// <param name="Count">How many settings were written; 0 on failure.</param>
/// <param name="Message">One line for an operator. Always set.</param>
/// <param name="Errors">
/// Why each refused setting was refused, by property name. Empty on success, and on a failure
/// that is not one setting's (an unknown effect, say).
/// </param>
public record EffectConfigurationResult(
    bool Success,
    int Count,
    string Message,
    IReadOnlyDictionary<string, string> Errors
);
