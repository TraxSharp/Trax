---
title: IEffectSettingsService
description: "Reference for IEffectSettingsService, which lists this process's effects, turns them on and off, and edits a configurable effect's settings."
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 21
package: Trax.Scheduler
namespace: Trax.Scheduler.Services.Effects
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/Effects/IEffectSettingsService.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/Effects/EffectSettingsService.cs
---

# IEffectSettingsService

Lists the observational effects registered in this process, turns them on and off, and edits a configurable effect's settings. The dashboard's effects page and the GraphQL API's effects query and mutations both call it, so the two show the same values, refuse the same changes and write a setting the same way. `AddScheduler(...)` registers the default implementation, `EffectSettingsService`, as scoped.

## Signature

```csharp fragment
namespace Trax.Scheduler.Services.Effects;

public interface IEffectSettingsService
{
    bool IsAvailable { get; }
    IReadOnlyList<EffectSettings> GetEffects();
    OperationResult SetEffectEnabled(string fullName, bool enabled);
    EffectConfigurationResult ConfigureEffect(string fullName, IReadOnlyDictionary<string, string?> values);
}
```

## Members

| Member | Description |
|---|---|
| `IsAvailable` | Whether the host registered an effect registry (`AddEffects` does). Without one, `GetEffects` is empty and every change is refused |
| `GetEffects()` | Every effect the registry tracks, ordered by the factory's full type name |
| `SetEffectEnabled(fullName, enabled)` | Turns the effect whose factory has this full type name on or off. Refused, with nothing changed, for a name the registry does not track or an effect it tracks as not toggleable. `Count` is 1 on success |
| `ConfigureEffect(fullName, values)` | Writes the named settings of a configurable effect, all or none (below) |

## EffectSettings

```csharp fragment
public record EffectSettings(
    string Name, string FullName, bool Enabled, bool Toggleable, bool IsConfigurable,
    string? ConfigurationTypeName, string? Configuration, IReadOnlyList<EffectSettingField> Fields);

public record EffectSettingField(
    string Name, string TypeName, EffectFieldKind Kind, bool Nullable, IReadOnlyList<string>? EnumValues,
    bool Sensitive, bool HasValue, string? Value, string Hint);

public enum EffectFieldKind { Boolean, Enum, Text, SetInCode }
```

`Configuration` is the settings object as JSON, with every member marked [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive) written as `{"_redacted": true}`; it is null when the settings type cannot be written as JSON (it holds a delegate, say). `Fields` describes each public read-write property of the settings type, editable ones first:

| Field | Meaning |
|---|---|
| `Name` | The property's name, which `ConfigureEffect` takes |
| `TypeName` | The property's type, without `Nullable<>`, such as `Int32` |
| `Kind` | `Boolean`, `Enum`, `Text` (a number, string, date, time, duration, `Guid` or `char`), or `SetInCode` for any other type, which cannot be written here |
| `Nullable` | Whether no value is allowed |
| `EnumValues` | The member names, for an enum |
| `Sensitive` | The property is marked `[TraxSensitive]`, on itself, on the record parameter it comes from, on an interface property it implements, or on its type |
| `HasValue` | Whether the setting holds a value |
| `Value` | The current value as text, in the form `ConfigureEffect` reads back. Null when there is none, for `SetInCode`, and always for a sensitive setting |
| `Hint` | What to type, such as `yyyy-MM-dd HH:mm:ss (UTC)` |

A sensitive setting's value is never returned. Writing one is allowed.

## ConfigureEffect

```csharp fragment
public record EffectConfigurationResult(
    bool Success, int Count, string Message, IReadOnlyDictionary<string, string> Errors);
```

Each value is text, read as its property's type: numbers in invariant culture with `.` for the decimal point and no thousands separators, a date or time with no offset as UTC, a boolean as `true` or `false`, an enum by member name. Null or blank is no value for a property that accepts null, the empty string for a non-nullable string, and refused for anything else. The value must then pass the property's own `ValidationAttribute`s.

Every value is read and checked before any is written. When one is refused, nothing is written and `Errors` names each refused setting with its reason; an unknown setting, and a `SetInCode` one, are refused the same way. When a setter throws part way, the settings already written are put back. A setting not named is not written, so a change made to it elsewhere in the meantime is kept. A change applies to the next run in this process.

## Behavior

Everything is per process. The effect registry and each effect's settings object live in memory,
with no persistence and no broadcast to other processes, so a change applies only to the process
that served the call. A dashboard or API host is often not where trains run, and a restart
restores the configured state.

## Example

Turning off the [parameter effect](/docs/effect/effect-providers/parameter-effect)'s saved outputs
in this process:

```csharp compile
using Trax.Scheduler.Services.Effects;

public class EffectSwitches(IEffectSettingsService effectSettings)
{
    public bool StopSavingOutputs()
    {
        var result = effectSettings.ConfigureEffect(
            "Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory.ParameterEffectProviderFactory",
            new Dictionary<string, string?> { ["SaveOutputs"] = "false" });
        return result.Success;
    }
}
```

## See also

- [Effect providers](/docs/effect/effect-providers)
- [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive)
- [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service)
