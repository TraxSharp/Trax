# Trax.Api

The GraphQL layer: schema generation over `[TraxQueryModel]` entities, authentication and
authorization, subscriptions, persisted operations, audit, and the cross-schema data
loaders. It sits above `Trax.Scheduler` and below `Trax.Dashboard` and `Trax.Samples`.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| anything in an `AddTrax*` extension | [0002](./docs/adr/0002-reading-the-service-collection-is-order-dependent.md), before you read the `IServiceCollection` |
| a new host-configuration surface | [0001](./docs/adr/0001-a-misconfigured-host-fails-at-startup.md), it needs a startup validator |
| subscriptions or socket auth | both, in that order, then [0006](./docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md), which removed the ordering from subscription auth. This is where the silent failure happened |
| query models: navigations, filter or sort inputs, `BindFields`/`ExposeAs` | [0025](./docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md): every entity a model reaches declares its posture, and the inputs follow the exposed field set and the target's gate |
| a type extension, type module or `ConfigureSchema` field | [0003](./docs/adr/0003-a-type-extension-field-declares-its-own-posture.md), the field declares its own posture unless its parent's gate covers it |
| the HTTP transport (GET, preflight headers, `ModifyServerOptions`) | [0024](./docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md), GET is off unless the host opts in |
| `MaxOperationsPerRequest`, or a new namespace field | [0029](./docs/adr/0029-the-operation-cap-counts-the-operations-under-each-namespace.md): the cap counts the fields under each namespace, so a field that only groups others must be marked with `NamespaceField` |
| persisted-operation ids, their caches, or cross-node invalidation | [0026](./docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md): an id runs only the stored document, and a host declares a broadcaster or `SingleNode()` |
| how a socket validates a JWT | [0022](./docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md): through the scheme's own handler, never a copy of its validation |
| the principal id, or anything keyed on it | [0023](./docs/adr/0023-a-principal-id-is-qualified-by-its-scheme.md): it is `{scheme}:{id}`, built only by `ToClaimsPrincipal` |
| `queueTrain` or `requeueExecution` | central `docs/0017`, they enqueue through the mediator so per-train authorization applies; manifest triggers and dead-letter requeues are governed by the operations gate |
| what `requeueExecution` replays | central `docs/0041`, the requeued run replays the decisions the execution recorded |
| `failureClass` on executions, or the `executions(failureClass:)` filter | central `docs/0020`, a failure is classified where it happens |
| `subjectKey` or `confirmedAt` on work queue reads | central `docs/0019` and `docs/0018` |

Decisions binding more than one repo live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by repo. Thirty-three name `api`. Among them: executable guards, exact version pinning, the
dependency direction, the three test conventions, the canonical train name being the
interface FullName, the documentation lints, feature-package tables shipping in the core
provider migration set, the public API baseline, test frameworks staying out of shipped
libraries, exemplars declared by attribute, Trax owning its vocabulary, tests owning their
timeouts, every `PackageVersion` naming a referenced package, a chain being a declaration
(`0016`), the enqueue, staging, subject and failure-classification decisions (`0017` to
`0020`), the shared dashboard/API operations (`0022`), and a requeue replaying decisions (`0041`). In a workspace checkout the index is at
`../Trax.Docs/adr/README.md`; that path does not resolve on GitHub, because it crosses a
repository boundary.

## When your change makes a decision

Most changes do not. When one does (reversing it would cost something real, a future reader
would ask why it is like this, and there were real alternatives), it takes five steps and
the build enforces four. The `adr-guard` job runs on every pull request.

| | Step | Enforced |
| --- | --- | --- |
| 1 | Notice you made a decision, and write the ADR | no, this is the human step |
| 2 | Tag it `areas`, and add it to `docs/adr/README.md` | yes |
| 3 | Say where it stands in `## Status` and record it in `## Changelog` | yes |
| 4 | Give it `## Exemplars`: guards, `**Enforced elsewhere:**`, or `**Unenforced:**` with a reason | yes |
| 5 | Have each guard you named cite the ADR back, in its docstring and its failure message | yes |

Step 1 is the only one you have to remember, because no test can detect a decision you chose
not to record. The format is
[`.claude/skills/recording-decisions/ADR-FORMAT.md`](./.claude/skills/recording-decisions/ADR-FORMAT.md).

## Guards and validators

`tests/Trax.Api.Tests.Meta/` holds fifteen convention guards. Fourteen are shared with other
repos and enforce workspace-wide rules, among them `WorkQueueCreationSitesTests`, which allows
no site in this repo to build a work queue row (`docs/0017`);
`NoSilentRegistrationOrderDependenceTests` is unique to this repo and is the census behind
[0002](./docs/adr/0002-reading-the-service-collection-is-order-dependent.md).

Seven runtime validators fail the host at startup rather than at request time: six under
`src/Trax.Api.GraphQL/Startup/` and `TraxGraphQLAuthPolicyValidator` alongside the
authorization code. They derive from `StartupGate` and check in `StartingAsync`, after the
container is complete and before Kestrel or any worker starts (Api ADR 0001).
`QueryModelScalarCollectionIndexValidator` sits beside them and is advisory: it logs a
missing-index warning and never blocks startup.

The census is on: every guard class under that folder is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason, and the `adr-guard` job checks it. A new guard is
unclassified until you choose, and the build says so. Opting out is a normal answer; a reason
that reads as a deferral is not.

## Running the tests

There is no compose file in this repo. Postgres and RabbitMQ come from the workspace's
sample stack, which starts both with the `trax`/`trax123` credentials the fixtures expect
and creates the four databases they hard-code:

```bash
docker compose -f ../Trax.Samples/docker-compose.yml up -d
dotnet test
```

Those four databases (`trax_api_operations`, `trax_api_workqueue`, `trax_api_logs`,
`trax_api_health`) come from the compose file's init script, which Postgres runs only when
the container is first created. Against a container that predates them, or a clone of this
repo on its own, there is no one-liner: bring up a `postgres:16` on 5432 and a
`rabbitmq:4-management` on 5672 with that user and password the way
`.github/workflows/pull_request.yml` does, then create the databases the way its "Create
per-fixture test databases" step does.

```bash
for db in trax_api_operations trax_api_workqueue trax_api_logs trax_api_health; do
  PGPASSWORD=trax123 psql -h localhost -U trax -d trax -c "CREATE DATABASE $db;"
done
```

`OperationsQueriesTests`, `WorkQueueOperationsTests`, `LogQueriesTests` and
`TraxHealthServiceTests` name those databases and do not create them, so they fail rather
than skip when one is missing. The AuthE2E suite provisions its own through
`AuthE2EHost.EnsureDatabaseExists`, so adding a fixture there needs no setup change, and
the persisted-operations integration tests skip when Postgres or the broker is unreachable.
