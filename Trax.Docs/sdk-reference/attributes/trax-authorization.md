---
title: TraxAuthorization
description: "Reference for TraxAuthorization and TraxAuthorizationDeclaration, which read the TraxAuthorize and TraxAllowAnonymous posture off a type or method."
type: reference
area: security
package: Trax.Effect
namespace: Trax.Effect.Attributes
source:
  - Trax.Effect/src/Trax.Effect/Attributes/TraxAuthorization.cs
parent: Attributes
grand_parent: SDK Reference
nav_order: 6
---

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible. See [API Security](/docs/api-security).

# TraxAuthorization

Reads the authorization posture a type or a method declares with [`[TraxAuthorize]`](/docs/sdk-reference/attributes/trax-authorize) and [`[TraxAllowAnonymous]`](/docs/sdk-reference/attributes/trax-allow-anonymous), and reports any authorization attribute from another framework found beside them. Trax.Api and the mediator read train, entity and resolver postures through it. Both types are public and hidden from completion.

## Signatures

```csharp fragment
namespace Trax.Effect.Attributes;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class TraxAuthorization
{
    public static IReadOnlyList<string> ForeignAuthorizationAttributes { get; }

    public static TraxAuthorizationDeclaration Read(Type type);
    public static TraxAuthorizationDeclaration Read(MethodInfo method);
    public static void Validate(TraxAuthorizationDeclaration declaration, string subject);
    public static string ForeignAttributeMessage(string subject, IReadOnlyList<string> foreignAttributes);
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record TraxAuthorizationDeclaration(
    IReadOnlyList<TraxAuthorizeAttribute> Authorize,
    bool AllowAnonymous,
    IReadOnlyList<string> ForeignAttributes
)
{
    public bool HasAuthorize { get; }
    public bool Declares { get; }
    public bool Conflicts { get; }
    public bool HasForeign { get; }
}
```

## Members

| `TraxAuthorization` member | Description |
|----------------------------|-------------|
| `ForeignAuthorizationAttributes` | The full names of the attributes Trax refuses to read: `HotChocolate.Authorization.AuthorizeAttribute`, `HotChocolate.Authorization.AllowAnonymousAttribute`, and their HotChocolate 15 names under `HotChocolate.AspNetCore.Authorization`. ASP.NET Core's `[Authorize]` is not on the list. |
| `Read(Type type)` | The declaration on a type: its own attributes, its base classes' and every implemented interface's. Each `TraxAuthorizeAttribute` instance is counted once. |
| `Read(MethodInfo method)` | The declaration on a method, inherited attributes included, so an override keeps its base method's posture |
| `Validate(declaration, subject)` | Throws `InvalidOperationException` with `ForeignAttributeMessage` when the declaration has a foreign attribute |
| `ForeignAttributeMessage(subject, foreignAttributes)` | The failure message for a surface that declared its posture with another framework's attribute, naming the attributes and the Trax replacements. `subject` describes the surface, such as `GraphQL field 'Issue.content' (IssueContentExtension.GetContent)`. |

| `TraxAuthorizationDeclaration` member | Description |
|---------------------------------------|-------------|
| `Authorize` | Every `[TraxAuthorize]` found |
| `AllowAnonymous` | Whether `[TraxAllowAnonymous]` is present |
| `ForeignAttributes` | The full names of foreign authorization attributes found, sorted |
| `HasAuthorize` | `Authorize` is not empty: the surface is gated |
| `Declares` | The surface states a posture, gated or public |
| `Conflicts` | Both markers are present |
| `HasForeign` | `ForeignAttributes` is not empty |

## Example

```csharp compile
using Trax.Effect.Attributes;

[TraxAuthorize(Roles = "Admin")]
public class AdminReport;

public static class PostureCheck
{
    public static void Check()
    {
        var declaration = TraxAuthorization.Read(typeof(AdminReport));

        TraxAuthorization.Validate(declaration, nameof(AdminReport));

        if (!declaration.Declares || declaration.Conflicts)
            throw new InvalidOperationException($"{nameof(AdminReport)} declares no single posture.");
    }
}
```

## See also

- [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) and [TraxAllowAnonymous](/docs/sdk-reference/attributes/trax-allow-anonymous)
- [Authorization](/docs/authorization)
