using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Services.Effects;

/// <summary>How a setting is edited.</summary>
public enum EffectFieldKind
{
    /// <summary>A switch: <see langword="true"/> or <see langword="false"/>.</summary>
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
