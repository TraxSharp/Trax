using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Services.Effects;

/// <summary>
/// Lists the observational effects registered in this process, turns them on and off, and edits
/// a configurable effect's settings. The dashboard's effects page and the GraphQL API's effects
/// query and mutations all call it, so both surfaces show the same values, refuse the same
/// changes and write a setting the same way (central <c>docs/0022</c>).
/// </summary>
/// <remarks>
/// <para>Everything here is per process. The effect registry and each effect's settings object
/// are in-memory singletons with no persistence and no broadcast to other processes, so a change
/// applies only to the process that served the call: a dashboard or API host is often not where
/// trains run, and a restart restores the configured state.</para>
///
/// <para>A settings property marked <c>[TraxSensitive]</c> is never read back: its current value
/// is left out of <see cref="EffectSettings.Fields"/> and masked in
/// <see cref="EffectSettings.Configuration"/>. Writing one is allowed.</para>
/// </remarks>
public interface IEffectSettingsService
{
    /// <summary>
    /// Whether the host registered an effect registry. Without one, <see cref="GetEffects"/> is
    /// empty and every change is refused.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Every effect the registry tracks, ordered by full name, with whether it runs, whether it
    /// can be toggled, and for a configurable one its settings.
    /// </summary>
    IReadOnlyList<EffectSettings> GetEffects();

    /// <summary>
    /// Turns the effect whose factory has this full type name on or off in this process.
    /// </summary>
    /// <returns>
    /// <c>OperationResult(true, Count: 1, ...)</c> once set, also when it already was.
    /// <c>OperationResult(false, ...)</c>, with nothing changed, when no registry is registered,
    /// no tracked effect has the name, or the registry tracks it as not toggleable.
    /// </returns>
    OperationResult SetEffectEnabled(string fullName, bool enabled);

    /// <summary>
    /// Writes the named settings of a configurable effect in this process, all or none: every
    /// value is read as its property's type and checked against the property's
    /// <see cref="System.ComponentModel.DataAnnotations.ValidationAttribute"/>s before any is
    /// written, and when a setter throws part way, the ones already written are put back. A
    /// setting not named is not written, so a change made elsewhere to it is kept.
    /// </summary>
    /// <remarks>
    /// Each value is text, read as <see cref="EffectSettingField"/> describes: numbers in
    /// invariant culture with <c>.</c> for the decimal point and no thousands separators, a date
    /// or time with no offset as UTC, a boolean as <c>true</c> or <c>false</c>, an enum by member
    /// name. Null or blank is no value for a property that accepts null, the empty string for a
    /// non-nullable string, and refused for anything else. An
    /// <see cref="EffectFieldKind.SetInCode"/> setting cannot be written. The change applies to the next
    /// run in this process.
    /// </remarks>
    /// <param name="fullName">The effect factory's full type name.</param>
    /// <param name="values">The settings to write, by property name, as text.</param>
    /// <returns>
    /// Success with <see cref="EffectConfigurationResult.Count"/> settings written, or a failure
    /// with nothing written and, where a setting was at fault, its reason in
    /// <see cref="EffectConfigurationResult.Errors"/>.
    /// </returns>
    EffectConfigurationResult ConfigureEffect(
        string fullName,
        IReadOnlyDictionary<string, string?> values
    );
}

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

/// <summary>How a setting is edited.</summary>
public enum EffectFieldKind
{
    /// <summary>A switch: <c>true</c> or <c>false</c>.</summary>
    Boolean = 0,

    /// <summary>One of <see cref="EffectSettingField.EnumValues"/>.</summary>
    Enum = 1,

    /// <summary>Text read as the property's type (a number, string, date, time, duration, GUID or character).</summary>
    Text = 2,

    /// <summary>
    /// A type with no text form (a delegate, a collection, an object): it is set in code and
    /// cannot be written here.
    /// </summary>
    SetInCode = 3,
}

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
