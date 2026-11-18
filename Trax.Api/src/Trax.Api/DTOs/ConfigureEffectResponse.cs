using Trax.Scheduler.Services.Effects;

namespace Trax.Api.DTOs;

/// <summary>What <c>operations.configureEffect</c> did. Nothing is written unless every value is accepted.</summary>
/// <param name="Success">Whether the settings were written.</param>
/// <param name="Count">How many settings were written; 0 on failure.</param>
/// <param name="Message">One line for an operator.</param>
/// <param name="Errors">
/// Why each refused setting was refused. Empty on success, and on a failure that is not one
/// setting's, such as an unknown effect.
/// </param>
public record ConfigureEffectResponse(
    bool Success,
    int Count,
    string Message,
    IReadOnlyList<EffectSettingError> Errors
);
