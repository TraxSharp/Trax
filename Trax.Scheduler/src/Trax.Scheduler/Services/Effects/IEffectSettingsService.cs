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
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/i-effect-settings-service">SDK reference</seealso>
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
    /// or time with no offset as UTC, a boolean as <see langword="true"/> or <see langword="false"/>, an enum by member
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
