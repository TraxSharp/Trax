---
layout: default
title: ITraxAuditRedactor
parent: API Audit
grand_parent: SDK Reference
---

# ITraxAuditRedactor

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Scrubs GraphQL variables before the listener hands them to the channel. Register a custom implementation with `services.AddSingleton<ITraxAuditRedactor, MyRedactor>()` before `AddAudit`.

## Signature

```csharp
public interface ITraxAuditRedactor
{
    IReadOnlyDictionary<string, object?>? Redact(IReadOnlyDictionary<string, object?>? variables);
}
```

Return `null` to omit variables entirely from the audit entry. The default implementation (`DefaultAuditRedactor`) passes variables through unchanged.

## Example

Redact keys by name:

```csharp
public sealed class KeyNameRedactor : ITraxAuditRedactor
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "apiKey", "secret", "ssn"
    };

    public IReadOnlyDictionary<string, object?>? Redact(IReadOnlyDictionary<string, object?>? variables)
    {
        if (variables is null) return null;
        return variables.ToDictionary(
            kv => kv.Key,
            kv => Sensitive.Contains(kv.Key) ? "[redacted]" : kv.Value
        );
    }
}
```
