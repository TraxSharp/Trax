using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Services.Effects;

/// <summary>An observational effect registered in this process, and its runtime state.</summary>
/// <param name="Name">The effect provider factory's type name.</param>
/// <param name="FullName">The factory's full type name, which identifies the effect.</param>
/// <param name="Enabled">Whether the effect runs in this process.</param>
/// <param name="Toggleable">Whether the registry lets the effect be turned on and off at runtime.</param>
/// <param name="IsConfigurable">
/// Whether the factory exposes runtime settings (it implements <c>IConfigurableProviderFactory</c>).
/// </param>
/// <param name="ConfigurationTypeName">The settings type's full name, when configurable.</param>
/// <param name="Configuration">
/// The settings as JSON, when configurable, with every <c>[TraxSensitive]</c> member written as
/// <c>{"_redacted": true}</c>; null when the settings type cannot be written as JSON.
/// </param>
/// <param name="Fields">
/// The settings' public read-write properties, editable ones first in declaration order; empty
/// when not configurable.
/// </param>
public record EffectSettings(
    string Name,
    string FullName,
    bool Enabled,
    bool Toggleable,
    bool IsConfigurable,
    string? ConfigurationTypeName,
    string? Configuration,
    IReadOnlyList<EffectSettingField> Fields
);
