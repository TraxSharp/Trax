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
