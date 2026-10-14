---
title: "operations: effect mutations"
description: "Reference for setEffectEnabled and configureEffect, the operations mutations that toggle and configure effects in the API process."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 5
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/OperationsMutations.cs
---

# operations: effect mutations

Fields of `OperationsMutations` that change the effects registered in the API process.

## setEffectEnabled

Turns an observational effect on or off in the API process, through `IEffectSettingsService.SetEffectEnabled` in Trax.Scheduler, the call the dashboard's [Effects page](/docs/dashboard/pages#effects-page) makes. The change is in memory: it does not reach the scheduler or worker processes where trains usually run, and a restart restores the configured state.

```graphql
mutation {
  operations {
    setEffectEnabled(
      fullName: "Trax.Effect.Provider.Json.Services.JsonEffectFactory.JsonEffectProviderFactory"
      enabled: false
    ) {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `fullName` | `String!` | Yes | The effect factory's full type name, as [`operations.effects`](/docs/sdk-reference/graphql-api/queries/operations#effects) reports it in `fullName`. Matched exactly. |
| `enabled` | `Boolean!` | Yes | `true` to turn the effect on, `false` to turn it off |

**Returns**: `OperationResponse`. `success` is false, and nothing changes, when no effect has that name, the effect was registered as not toggleable, or the host registers no effect registry. On success `count` is 1.

## configureEffect

Writes settings of a configurable effect in the API process, through `IEffectSettingsService.ConfigureEffect` in Trax.Scheduler, the call the dashboard's Configure dialog makes. [`effects`](/docs/sdk-reference/graphql-api/queries/operations#effects) lists each effect's `fields`: their names, how each is edited and what to type. Send only the settings you change; a setting not listed is not written, so a change made elsewhere to it is kept.

```graphql
mutation {
  operations {
    configureEffect(
      fullName: "Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory.ParameterEffectProviderFactory"
      values: [
        { name: "SaveOutputs", value: "false" }
        { name: "MaxParameterBytes", value: null }
      ]
    ) {
      success
      count
      message
      errors { field message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `fullName` | `String!` | Yes | The effect factory's full type name, as `effects` reports it. Matched exactly |
| `values` | `[EffectSettingValueInput!]!` | Yes | 1 to 1000 `{ name: String!, value: String }`, each setting at most once |

Each `value` is text read as the setting's type: numbers in invariant culture with `.` for the decimal point and no thousands separators, a date or time with no offset as UTC, a boolean as `true` or `false`, an enum by member name. Null or blank is no value for a setting that accepts null, the empty string for a text setting that does not, and refused for anything else. A `SET_IN_CODE` setting cannot be written. A `[TraxSensitive]` setting can be written, though it is never read back.

It is all or nothing: every value is read and checked against the setting's validation attributes before any is written, and when one is refused, or a setter throws part way, nothing is written.

**Returns**: `ConfigureEffectResponse`:

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether the settings were written |
| `count` | `Int!` | How many settings were written; `0` on failure |
| `message` | `String!` | One line for an operator |
| `errors` | `[EffectSettingError!]!` | `{ field, message }` for each refused setting: no such setting, set in code, given twice, not readable as its type, or failing its validation. Empty on success, and on a failure that is not one setting's, such as an unknown effect or one with no settings |

The change is in memory and applies to the next run in this process. It does not reach the scheduler or worker processes where trains usually run, and a restart restores the configured settings.

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations: system queries](/docs/sdk-reference/graphql-api/queries/operations)
