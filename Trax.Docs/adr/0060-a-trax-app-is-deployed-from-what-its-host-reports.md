---
authors: [Theauxm]
repos: [cli, effect, scheduler, api, samples]
areas: [platform, packaging]
status: accepted
---

# A Trax app is deployed from what its host reports, onto infrastructure the trax tool generates

`trax deploy` takes a Trax app to a running deployment with one command, AWS first and a compose stack for any other
host. The shape of that deployment comes from the host itself: started with `--trax-export-deploy`, it runs its
startup checks and writes a `DeployManifest` naming its roles, its database, its transports, its endpoints and its
authentication schemes. A small file beside the solution holds only the choices that belong to the target (region,
environments, profile, domain, sizes, the names of the app's secrets). The tool turns both into a CloudFormation
template it generates itself and applies through the AWS SDK for .NET. Decided 2026-10-20.

## Status

**Accepted.**

## Why the host reports its own shape

`Program.cs` already says whether the host keeps its runs in Postgres, runs a scheduler or a worker, connects to
RabbitMQ, maps the console and which authentication it accepts. A deploy file that restated it would drift from the
code the first time either changed, and the drift would show up as a deployment that starts and then fails. Reporting
it from the host also means every refusal the host makes at startup (demo credentials outside Development, an
operations namespace with no gate) stops a deploy before anything is created, without the tool keeping a second copy
of those rules.

The cost is that every registration adding something a deployment must provide records it. A package that adds a
dependency on infrastructure and records nothing produces a manifest that leaves it out.

## Why the tool generates CloudFormation

Considered and rejected:

- **CDK**, including AWS's own .NET deploy tool (`dotnet aws deploy`) and .NET Aspire's AWS publisher, which both
  generate CDK. Every CDK app needs Node 22 on the deploying machine and a bootstrap stack in the account, for a
  framework whose users otherwise need only the .NET SDK. Aspire's AWS publishing is also still a preview.
- **Terraform**: a second tool to install and a state file to keep somewhere safe.
- **ECS Express Mode** for the hub: it scales a service between 1 and 20 tasks, and a Trax hub runs as exactly one
  until hosts hold leases on their runs; it serves HTTP only, so the workers would need the other model anyway.
- **App Runner**: closed to new customers on 2026-04-30.

CloudFormation needs nothing installed beyond the SDK, keeps the stack's state in AWS, and `trax deploy plan` writes
the template as a file that can be read and diffed before anything changes. The cost is that Trax maintains the
template: every resource, every security group rule and every IAM statement is Trax's to get right.

## Consequences

- A deployed stack is managed: each `trax deploy` reconciles it with the manifest and the host's report, and a change
  made by hand in the console is reverted.
- One hub, any number of workers, until a hub's restart stops failing runs that are live on other hosts.
- A host that is deployed migrates in one process started with `--trax-migrate`, and every serving process runs with
  `TRAX_MIGRATIONS=skip`.

## Exemplars

**Enforced elsewhere:** `DeployManifestExportTests` in Trax.Api (`tests/Trax.Api.Tests/Deploy`) checks that a host
reports its endpoints, its authentication and its role without serving, and refuses the export when a startup check
refuses the host. `TraxDeployExportTests` in Trax.Effect (`tests/Trax.Effect.Tests.Integration`) checks what a data
provider reports and that an exporting process skips its database and refuses to serve.

Not covered: nothing checks that a new registration which needs infrastructure records it in the manifest; review
does.

## Changelog

- **2026-10-20**: Recorded.
