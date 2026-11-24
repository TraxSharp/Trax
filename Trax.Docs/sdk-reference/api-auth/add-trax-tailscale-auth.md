---
title: AddTraxTailscaleAuth
description: "Reference for AddTraxTailscaleAuth: sign-in by tailnet login, trusted only on a request carrying the proxy secret trax deploy minted, with roles by login."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 22
package: Trax.Api.Auth.Tailscale
namespace: Trax.Api.Auth.Tailscale
source:
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleAuthServiceCollectionExtensions.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleBuilder.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleAuthenticationOptions.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleDefaults.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleAuthHandler.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleTrustValidator.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleProxySecret.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleHeaderValue.cs
  - Trax.Api/src/Trax.Api.Auth.Tailscale/TailscaleLogins.cs
---

# AddTraxTailscaleAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

An extension method on `IServiceCollection`, declared on `TailscaleAuthServiceCollectionExtensions`, that registers
sign-in by tailnet identity: the caller is the login [Tailscale serve](https://tailscale.com/kb/1312/serve) puts in
`Tailscale-User-Login`, with the roles the host maps that login to. It is for a hub deployed by `trax deploy` with
`"exposure": "tailnet"`, and refuses to start anywhere else. Why the headers can be trusted there and nowhere else is
in [Tailnet sign-in](/docs/security/tailnet-sign-in).

## Signatures

```csharp fragment
public static AuthenticationBuilder AddTraxTailscaleAuth(
    this IServiceCollection services,
    Action<TailscaleBuilder> configure,
    Action<TailscaleAuthenticationOptions>? configureOptions = null);
```

```csharp fragment
public sealed class TailscaleBuilder
{
    public TailscaleBuilder Login(string login, params string[] roles);
    public TailscaleBuilder AnyLogin(params string[] roles);
}

public sealed class TailscaleAuthenticationOptions : AuthenticationSchemeOptions
{
    public string? ProxySecret { get; set; }
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `configure` | `Action<TailscaleBuilder>` | Names the logins admitted and their roles. At least one `Login` or an `AnyLogin` is required; a builder that admits nobody throws `InvalidOperationException` at registration. |
| `configureOptions` | `Action<TailscaleAuthenticationOptions>?` | Sets `ProxySecret` from somewhere other than configuration. |

## Members

`TailscaleBuilder`, passed to `configure`:

| Member | Description |
|---|---|
| `Login(string login, params string[] roles)` | Admits `login` (`alice@example.com`, as Tailscale reports it) with `roles`. Compared ignoring case. A login with a comma or surrounding space, or one mapped twice, throws `ArgumentException`. |
| `AnyLogin(params string[] roles)` | Admits every other login on the tailnet with `roles`, including anyone a device was shared with. Without it, a login not named with `Login` is refused. |

`TailscaleAuthenticationOptions`:

| Member | Description |
|---|---|
| `ProxySecret` | The secret the proxy sends in `Trax-Proxy-Secret`. When it is not set in code it is read from configuration under `Trax:Deploy:ProxySecret` ([`DeployExposure.ProxySecretKey`](/docs/sdk-reference/configuration/deploy-exposure)), where `trax deploy` puts it. |

`TailscaleDefaults`:

| Constant | Value |
|---|---|
| `SchemeName` | `TraxTailscale` |
| `PolicyName` | `TailscalePolicy` |
| `LoginHeader` | `Tailscale-User-Login` |
| `NameHeader` | `Tailscale-User-Name` |
| `ProfilePictureHeader` | `Tailscale-User-Profile-Pic` |
| `FunnelHeader` | `Tailscale-Funnel-Request` |
| `ProfilePictureClaim` | `picture`, the claim the profile picture's address is carried in |
| `PrincipalType` | `tailscale` |
| `MinimumProxySecretLength` | `32` |

## Returns

The `AuthenticationBuilder` from `services.AddAuthentication()`, for chaining further schemes.

## Behavior

### The host starts only where it is exposed on a tailnet

A host that registers the scheme refuses to start, in every environment including Development, unless:

- configuration has `Trax:Deploy:Exposure` set to exactly `tailnet`. `trax deploy` sets it (as
  `Trax__Deploy__Exposure`) only on the hub of a kubernetes environment with `"exposure": "tailnet"`, the one place it
  renders a proxy that proves itself to the host. The host cannot see from the inside whether it is on a public
  address, so it takes the deployment's word, and the deployment refuses to give it anywhere else; and
- the proxy secret is configured and at least 32 characters. `trax deploy` mints a new one on every deploy.

```
AddTraxTailscaleAuth() trusts Tailscale identity headers, and this host's exposure is not set. It starts only where
Trax:Deploy:Exposure is 'tailnet', ...
```

The refusal never repeats the secret. `trax deploy` refuses, before it builds anything, a host that registers this
scheme (by its name, or by its handler) on any other target: compose, aws, a kubernetes environment without the
exposure, or a service that is not the exposed hub. A deploy export (`--trax-export-deploy`) runs the same check,
and `trax deploy` runs the export with placeholder values for both, so the export reports the scheme and the target
decides.

### Authentication results

The scheme never reads the address a request came from, so neither a forwarded-for header nor a sender on a network
`UseForwardedHeaders` trusts can make a request look like the proxy's. What decides is the secret.

| Request | Result |
|---|---|
| No `Trax-Proxy-Secret` header | `NoResult()`: anonymous, whatever identity headers it carries. |
| `Trax-Proxy-Secret` more than once, or with a value that is not the secret | `NoResult()`, and a Warning that names the header and not the value. |
| The secret, and `Tailscale-Funnel-Request` | `NoResult()`: Funnel traffic comes from outside the tailnet and has no identity. |
| The secret, and no `Tailscale-User-Login` | `NoResult()`. Tailscale sends no identity for a request from a tagged device, or from the proxy's own host. |
| The secret, and `Tailscale-User-Login` more than once | `Fail("Multiple tailnet logins presented.")` |
| The secret, and one login value holding a comma | `Fail("Ambiguous tailnet login header.")`, as a proxy that joins duplicate headers would write them. |
| The secret, and a login that is not valid Q-encoded UTF-8 where it is encoded | `Fail("Unreadable tailnet login header.")` |
| The secret, and a login the builder does not admit | `Fail("Tailnet login not admitted.")`, and a Warning naming the login. |
| The secret, and an admitted login | `Success`, under the `TraxTailscale` scheme. |

The secret is compared as the SHA-256 digests of both values with `CryptographicOperations.FixedTimeEquals`, so the
comparison takes the same time whichever byte differs and whatever the length of the guess.

Tailscale writes a header value that is not ASCII as RFC 2047 Q-encoded UTF-8 (`=?utf-8?q?Ren=C3=A9?=`); the scheme
decodes the login and the display name. The principal's id is the login, its display name `Tailscale-User-Name`
(the login when there is none), its `PrincipalType` `tailscale`, and `Tailscale-User-Profile-Pic`, when it is one
absolute `https` or `http` address, the `picture` claim. Its qualified id is `TraxTailscale:<login>`. See
[Qualified principal ids](/docs/migration-guides/qualified-principal-ids).

### Roles come from logins, not tags

Tailscale's documentation says the identity headers are not populated for traffic from tagged devices, and serve's
code returns before setting them for a tagged source node. A tag therefore cannot be mapped to a role: a request
from a tagged device is anonymous. Map logins, or use another scheme (an API key) for tagged machines.

### Policies and default scheme

`AddTraxTailscaleAuth` registers the policy `TailscalePolicy`, satisfied only by a caller this scheme authenticated,
and adds the scheme to the combined `TraxAuthClaimTypes.TraxAuthPolicy`. It sets no default scheme. The Trax GraphQL
endpoint authenticates each HTTP request against every registered scheme, so `[TraxAuthorize]` and the builder's
`RequireAuthorization()` see a tailnet caller. A subscription socket is authenticated by the token schemes through
`connection_init`, and this scheme has no token: a host whose subscriptions need the tailnet caller makes it the
default scheme with `services.AddAuthentication(TailscaleDefaults.SchemeName)`, so the upgrade request is
authenticated like any other.

### Registered services

| Service | Lifetime |
|---|---|
| Authentication services and the `TraxTailscale` scheme with its handler | |
| The admitted logins and their roles | Singleton |
| `TailscalePolicy`, and `TraxTailscale` in `TraxAuthPolicy` | |
| [`TraxPrincipal` and `TraxCaller`](/docs/sdk-reference/api-auth/add-trax-principal-accessor), through `AddTraxPrincipalAccessor()` | Scoped |
| The startup check on the exposure and the secret, registered once | Hosted service |

## Examples

```csharp fragment
services.AddTraxTailscaleAuth(tailnet => tailnet
    .Login("alice@example.com", "Admin", "Operator")
    .Login("bob@example.com", "Operator"));
```

Every login on the tailnet as a reader, a few as more:

```csharp fragment
services.AddTraxTailscaleAuth(tailnet => tailnet
    .Login("alice@example.com", "Admin")
    .AnyLogin("Reader"));
```

## See also

- [Tailnet sign-in](/docs/security/tailnet-sign-in): the trust boundary, and what each part of it stops.
- [`trax deploy`, exposed on a tailnet](/docs/reference/cli#exposed-on-a-tailnet): what the deploy renders.
- [DeployExposure](/docs/sdk-reference/configuration/deploy-exposure): the configuration the deploy sets.
- [Startup refusals](/docs/security/startup-refusals)
