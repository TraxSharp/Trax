using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Services.Effects;

/// <summary>One setting of a configurable effect.</summary>
/// <param name="Name">The property's name, which <see cref="IEffectSettingsService.ConfigureEffect"/> takes.</param>
/// <param name="TypeName">The property's type name, without <c>Nullable&lt;&gt;</c>, such as <c>Int32</c>.</param>
/// <param name="Kind">How the setting is edited.</param>
/// <param name="Nullable">Whether no value is allowed (blank writes null).</param>
/// <param name="EnumValues">The member names, for an enum; null otherwise.</param>
/// <param name="Sensitive">
/// True when the property is marked <c>[TraxSensitive]</c>: its value is never returned.
/// </param>
/// <param name="HasValue">Whether the setting holds a value (is not null).</param>
/// <param name="Value">
/// The current value as text, in the form <see cref="IEffectSettingsService.ConfigureEffect"/>
/// reads back; null when there is none, when the setting is <see cref="Sensitive"/>, and for
/// <see cref="EffectFieldKind.SetInCode"/>.
/// </param>
/// <param name="Hint">What to type, such as <c>yyyy-MM-dd HH:mm:ss (UTC)</c>.</param>
public record EffectSettingField(
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
