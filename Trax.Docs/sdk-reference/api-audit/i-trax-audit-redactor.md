---
layout: default
title: ITraxAuditRedactor
description: Reference for ITraxAuditRedactor, which decides which GraphQL variables an audit entry records; the default records none.
parent: API Audit
grand_parent: SDK Reference
---

# ITraxAuditRedactor

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Decides which GraphQL variables, if any, an audit entry records. Variables carry whatever the caller sent, so the default records none of them: a host that wants them registers its own redactor with `services.AddSingleton<ITraxAuditRedactor, MyRedactor>()` and chooses what to keep.

## Signature

```csharp
public interface ITraxAuditRedactor
{
    JsonObject? Redact(JsonObject? variables);
}
```

`variables` is a `System.Text.Json.Nodes.JsonObject` built for this call, or `null` when the request had none. An input object is a nested `JsonObject` and a list a `JsonArray`, so a field such as `$input.password` can be found at any depth. Change the object in place and return it, return a different one, or return `null` to record no variables. If the redactor throws, the entry is recorded without variables.

The default implementation, `DefaultAuditRedactor`, returns `null`: no variables are recorded.

Literal values written in the document itself never reach the redactor or the entry. The listener replaces every string with `""` and every number with `0` before it records the document. See [API Security](/docs/api-security#what-an-entry-records).

## Example

Keep the variables, minus a set of sensitive fields at any depth:

```csharp
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

A removal list misses a field nobody thought of. Where that matters, copy across only the fields you mean to record.
