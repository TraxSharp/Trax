---
title: AddTraxAwsSecretsAsync
description: "Reference for AddTraxAwsSecretsAsync, which reads a host's secrets from AWS Secrets Manager into its configuration at start."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 53
package: Trax.Effect.Configuration.Aws
namespace: Trax.Effect.Configuration.Aws
source:
  - Trax.Effect/src/Trax.Effect.Configuration.Aws/TraxAwsSecretsExtensions.cs
---

# AddTraxAwsSecretsAsync

Reads a host's secrets from AWS Secrets Manager into its configuration before the host is built, with the
credentials the process already has. On EKS that is the Pod Identity role `trax deploy` gives the pod, so the values
never become Kubernetes Secrets and never pass through the cluster's store. It is in its own package, so the AWS SDK
reaches only the hosts that reference it.

## Signature

```csharp fragment
public static class TraxAwsSecretsExtensions
{
    public const string Section = "Trax:AwsSecrets";
    public const string SourceName = "aws-secrets";

    public static Task<IConfigurationManager> AddTraxAwsSecretsAsync(
        this IConfigurationManager configuration,
        IAmazonSecretsManager? client = null,
        CancellationToken cancellationToken = default
    );
}
```

## Parameters

| Parameter | What it is |
|---|---|
| `configuration` | The host builder's configuration (`builder.Configuration`). |
| `client` | The Secrets Manager client. Null builds one from the process's credentials and region, the usual AWS SDK chain. |
| `cancellationToken` | Stops the reads. |

## Behavior

Call it before the host is built:

```csharp fragment
var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.AddTraxAwsSecretsAsync();
```

It reads the entries under `Trax:AwsSecrets`, which `trax deploy` writes into the pod's environment. Each has:

| Field | What it means |
|---|---|
| `SecretId` | The secret's name or ARN. Required. |
| `JsonKey` | When the secret is a JSON object, the property to read (`password` of an RDS secret). |
| `Key` | The configuration key the value is set under: `Stripe:ApiKey`, or `Stripe__ApiKey` as an environment variable names it. |
| `PasswordOf` | In place of `Key`: a connection string name. The value is set as the `Password` of `ConnectionStrings:<name>`, quoted, so a `;` or `'` in it cannot cut the string. |

```bash
Trax__AwsSecrets__0__SecretId=orders-staging/app/Stripe__ApiKey
Trax__AwsSecrets__0__Key=Stripe__ApiKey
Trax__AwsSecrets__1__SecretId=arn:aws:secretsmanager:us-east-1:123456789012:secret:rds-orders
Trax__AwsSecrets__1__JsonKey=password
Trax__AwsSecrets__1__PasswordOf=Trax
```

The values are added above the configuration's other sources, so they win over the environment. A secret two
entries read is fetched once. An entry it cannot satisfy (no `SecretId`, neither `Key` nor `PasswordOf`, a JSON
property the secret lacks, a connection string that is not set) throws `InvalidOperationException` naming the
entry, and so does a secret the role may not read.

Under `--trax-export-deploy` it reads nothing, so an export needs no AWS credentials. Either way it marks
`Trax:Deploy:SecretSources:aws-secrets` in the configuration, which the host's
[DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) reports as `secretSources: ["aws-secrets"]`.
A Kubernetes environment on EKS refuses a host whose manifest lacks it.

## See also

- [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest)
- [trax deploy on EKS](/docs/reference/cli#on-eks)
