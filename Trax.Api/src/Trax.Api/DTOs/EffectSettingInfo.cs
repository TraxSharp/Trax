using Trax.Scheduler.Services.Effects;

namespace Trax.Api.DTOs;

/// <summary>
/// One setting of a configurable effect: what it is, how it is edited, and its current value
/// unless it is sensitive.
/// </summary>
/// <param name="Name">The property's name, which <c>operations.configureEffect</c> takes.</param>
/// <param name="TypeName">The property's type name, without <c>Nullable&lt;&gt;</c>, such as <c>Int32</c>.</param>
/// <param name="Kind">How the setting is edited: a switch, one of <paramref name="EnumValues"/>, text, or not here at all.</param>
/// <param name="Nullable">Whether the setting accepts no value; a null or blank value writes null.</param>
/// <param name="EnumValues">The member names, for an enum; null otherwise.</param>
/// <param name="Sensitive">
/// True when the property is marked <c>[TraxSensitive]</c>: <paramref name="Value"/> is always
/// null. It can still be written.
/// </param>
/// <param name="HasValue">Whether the setting holds a value (is not null). True for a sensitive setting that holds one.</param>
/// <param name="Value">
/// The current value as text, in the form <c>configureEffect</c> reads back; null when there is
/// none, when the setting is sensitive, and for <c>SET_IN_CODE</c>.
/// </param>
/// <param name="Hint">What to type, such as <c>yyyy-MM-dd HH:mm:ss (UTC)</c>.</param>
public record EffectSettingInfo(
    string Name,
    string TypeName,
    EffectFieldKind Kind,
    bool Nullable,
    IReadOnlyList<string>? EnumValues,
    bool Sensitive,
    bool HasValue,
    string? Value,
    string Hint
);

/// <summary>One setting to write with <c>operations.configureEffect</c>.</summary>
/// <param name="Name">The setting's name, as <see cref="EffectSettingInfo.Name"/> gives it.</param>
/// <param name="Value">
/// The value as text, read as the setting's type: numbers in invariant culture, a date or time
/// with no offset as UTC, a boolean as <c>true</c> or <c>false</c>, an enum by member name. Null
/// or blank is no value for a setting that accepts null and the empty string for a text setting
/// that does not.
/// </param>
public record EffectSettingValueInput(string Name, string? Value);

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

/// <summary>A setting <c>operations.configureEffect</c> refused, and why.</summary>
/// <param name="Field">The setting's name, as it was given.</param>
/// <param name="Message">Why it was refused.</param>
public record EffectSettingError(string Field, string Message);
