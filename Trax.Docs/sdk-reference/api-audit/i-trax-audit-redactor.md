---
title: ITraxAuditRedactor
description: Reference for ITraxAuditRedactor, which decides which GraphQL variables an audit entry records, and DefaultAuditRedactor, which records none.
type: reference
area: security
parent: API Audit
grand_parent: SDK Reference
nav_order: 2
package: Trax.Api.GraphQL.Audit
namespace: Trax.Api.GraphQL.Audit
source:
  - Trax.Api/src/Trax.Api.GraphQL.Audit/ITraxAuditRedactor.cs
---

# ITraxAuditRedactor

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Decides which GraphQL variables, if any, an audit entry records. `AddAudit` registers `DefaultAuditRedactor`, which records none, unless the host has registered its own.

## Signature

```csharp fragment
public interface ITraxAuditRedactor
{
    JsonObject? Redact(JsonObject? variables);
}

public sealed class DefaultAuditRedactor : ITraxAuditRedactor
{
    public JsonObject? Redact(JsonObject? variables);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `variables` | `System.Text.Json.Nodes.JsonObject?` | The request's variables, as an object built for this call, or `null` when the request had none. An input object is a nested `JsonObject` and a list a `JsonArray`. |

## Returns

The variables to record in [TraxAuditEntry.Variables](/docs/sdk-reference/api-audit/trax-audit-entry), or `null` to record none. The redactor may change `variables` in place and return it, or return a different object. `DefaultAuditRedactor` returns `null`.

## Behavior

- Called by the listener once for each request it records, and not for one that `SkipIntrospection` or `SkipSubscriptions` drops. The entries for a socket refused at `connection_init` and for a failed subscription event carry no variables and do not call it.
- When the redactor throws, the entry is recorded without variables.
- Literal values written in the document never reach the redactor or the entry: the listener replaces every string with `""` and every number with `0`, directive arguments included, before it records the document.
- Error messages are not recorded unless `TraxAuditOptions.RecordErrorMessages` is set, and the redactor never sees them.
- `AddAudit` registers `DefaultAuditRedactor` with `TryAdd`, so a redactor registered before or after `AddAudit` with `services.AddSingleton<ITraxAuditRedactor, T>()` replaces it.

## Example

Keep the variables, minus a set of sensitive fields at any depth:

```csharp compile
services.AddSingleton<ITraxAuditRedactor, SensitiveFieldRedactor>();

public sealed class SensitiveFieldRedactor : ITraxAuditRedactor
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "apiKey", "secret", "ssn",
    };

    public JsonObject? Redact(JsonObject? variables)
    {
        Strip(variables);
        return variables;
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(Sensitive.Contains).ToList())
                    obj.Remove(key);
                foreach (var (_, child) in obj)
                    Strip(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    Strip(child);
                break;
        }
    }
}
```

A removal list keeps any field it does not name. An allow-list, copying across only the fields to record, does not.

## See also

- [What an entry records](/docs/api-security#what-an-entry-records)
- [AddAudit](/docs/sdk-reference/api-audit/add-audit), [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry)
