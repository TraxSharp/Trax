---
title: DeployExposure
description: Reference for DeployExposure, what trax deploy tells a hub about how it is reached, and the secret its tailnet proxy proves itself with.
type: reference
area: security
parent: Configuration
grand_parent: SDK Reference
nav_order: 18
package: Trax.Effect
namespace: Trax.Effect.Configuration.Deploy
source:
  - Trax.Effect/src/Trax.Effect/Configuration/Deploy/DeployExposure.cs
---

# DeployExposure

Constants for what a deployment tells a host about how it is reached. A host cannot see from the inside whether it
is on a public address or behind a proxy, so `trax deploy` says so in its environment: it sets `Variable` to
`Tailnet`, and `ProxySecretVariable` to a secret it mints on every deploy, only on the hub of a kubernetes environment
with `"exposure": "tailnet"`, where it also renders the proxy that sends that secret. [Tailnet
sign-in](/docs/sdk-reference/api-auth/add-trax-tailscale-auth) reads both.

## Members

| Constant | Value | Meaning |
|---|---|---|
| `ConfigurationKey` | `Trax:Deploy:Exposure` | Where a host reads its exposure. |
| `Variable` | `Trax__Deploy__Exposure` | The environment variable `trax deploy` sets. |
| `Tailnet` | `tailnet` | Reached only through a Tailscale proxy the deployment controls: no public address, no published host port. |
| `ProxySecretKey` | `Trax:Deploy:ProxySecret` | Where a host reads the proxy's secret. |
| `ProxySecretVariable` | `Trax__Deploy__ProxySecret` | The environment variable that carries it, from the Secret `<app>-<env>-tailnet`. |
| `ProxySecretHeader` | `Trax-Proxy-Secret` | The request header the proxy sets, replacing any copy the caller sent. |
| `TailscaleScheme` | `TraxTailscale` | The tailnet sign-in scheme's name as a host reports it in its [deploy manifest](/docs/sdk-reference/configuration/deploy-manifest), by which `trax deploy` refuses a target that is not exposed on a tailnet. |

`trax.deploy.json` may not name anything under `Trax__Deploy__` in `settings` or `secrets`: an app that set the
exposure itself could switch tailnet sign-in on where nothing protects it.

## See also

- [AddTraxTailscaleAuth](/docs/sdk-reference/api-auth/add-trax-tailscale-auth)
- [`trax deploy`, exposed on a tailnet](/docs/reference/cli#exposed-on-a-tailnet)
- [Tailnet sign-in](/docs/security/tailnet-sign-in)
