using Trax.Scheduler.Services.Effects;

namespace Trax.Api.DTOs;

/// <summary>One setting to write with <c>operations.configureEffect</c>.</summary>
/// <param name="Name">The setting's name, as <see cref="EffectSettingInfo.Name"/> gives it.</param>
/// <param name="Value">
/// The value as text, read as the setting's type: numbers in invariant culture, a date or time
/// with no offset as UTC, a boolean as <see langword="true"/> or <see langword="false"/>, an enum by member name. Null
/// or blank is no value for a setting that accepts null and the empty string for a text setting
/// that does not.
/// </param>
public record EffectSettingValueInput(string Name, string? Value);
