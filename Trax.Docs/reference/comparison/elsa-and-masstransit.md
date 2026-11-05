---
title: Trax compared with Elsa and MassTransit
description: "How Trax compares with Elsa Workflows 3 and MassTransit saga state machines, and when to choose each."
type: explanation
area: general
nav_order: 4
---

# Trax compared with Elsa and MassTransit

Checked on 2026-11-05 against the Elsa 3 docs and the MassTransit 9 docs. Corrections are welcome as issues.

[Elsa Workflows 3](https://github.com/elsa-workflows/elsa-core) is an MIT-licensed workflow engine you embed in a .NET
app. Workflows are written in C#, as JSON, or in Elsa Studio, a visual designer. An instance suspends at a bookmark (a
delay, a timer, an event, an HTTP endpoint) and resumes when something triggers it.
[MassTransit](https://masstransit.massient.com/) is a .NET message bus. Its saga state machines react to messages
from a broker, correlated to an instance by id, and routing slips run multi-step work with compensation. MassTransit 9
and later [need a paid licence](https://massient.com); version 8 stays open source, and its commercial support ends on
2026-12-31.

Trax sits between them. Its state machines are declared in C# like a MassTransit saga, but they are driven by
callers, through a typed API with a rule on every event, rather than by messages from other services.

## Authoring

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| Model | Trains (typed step chains) and server-side state machines | An activity graph: flowchart, sequence or state machine | A saga state machine on message events; routing slips for step sequences |
| Written in | C# | C#, JSON, or the Elsa Studio designer | C# |
| Visual designer | No. The operations console and `trax machine show` (Mermaid, DOT) draw a machine you wrote | Yes | No. A state machine visualizer package, and the 9.2 dashboard preview |
| Change a workflow without redeploying | No | Yes, from JSON or the designer | No |
| Guards | Declarative rules, or C# delegates | Activities and expressions in C#, JavaScript, Python or Liquid | C# `If` and `Then` in `During` and `Initially` |
| Built-in activities | None: your junctions. A model-backed decider ships | HTTP, email, scheduling, messaging and more | Publish, send and request |
| Passing data between steps | Each junction's output reaches later junctions by its type, with no wiring | Workflow variables bound to activity inputs and outputs | Saga instance properties; routing slip variables |
| Branching | `Switch`, `Gate` and `Scale` declare every track; a decider picks one per run | Flowchart branches, `If`, `Switch` | `If` and `IfElse` in a state machine |
| Decisions by a model | A decider can be a rule, a lookup or a model, with a confidence floor per track and a second decider in shadow | Agent activities on Semantic Kernel | Not covered here |
| Parallel steps | `Parallel(...)`: fixed branches on copies of the run's data, joined before the next step | `Fork`, `Join`, `Parallel`, `ParallelForEach` | Not covered here: routing slips run in sequence |
| Checked before it runs | The host refuses to start if a chain can never run, naming the step and the missing type | Not covered here | Not covered here |
| In the IDE | VS Code and Rider hints showing each junction's input and output | Not covered here | An analyzer for message initializers |

## Long-running instances

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| Between steps | A stored snapshot, written after every move | A bookmark and a stored instance | A saga row, between messages |
| Timers | A state's `After(...)`, fired by the server | `Delay`, `Timer`, `Cron` and `StartAt`, on a local scheduler, Quartz or Hangfire | `Schedule`, which needs Quartz, Hangfire or a transport's scheduler |
| Other work | A state invokes a train or spawns a child machine | Dispatch a workflow, child workflows | Publish, send, request, routing slips |
| Compensation | Declared by you: a reset or compensating edge ([Undo an effect](/docs/statemachine/undo-an-effect)) | Not covered here | Routing slips compensate in reverse order |
| New versions under running instances | `Version` and `MigrateFrom` migrate each stored snapshot forward when it is loaded | Instances keep their version; alteration plans move them | Change the saga class and its data yourself |
| Resume a failed run | From its last checkpoint, by an operator or over GraphQL; a requeue can replay the recorded decisions | Incidents; alterations can reschedule an activity | Redelivery and retry of the message |

## Who may drive an instance

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| From outside | Typed GraphQL operations per machine: start, each event, get, watch, discard | An HTTP endpoint activity; a resume URL carrying a token; an authenticated event trigger | None built in: your API publishes a message |
| Rules per event | Yes: `Requires`, `RequiresPolicy`, `AnyAuthenticated`, `Anonymous`; reads can be `OwnerOnly` | No. API permissions cover a whole resource | No. Your API checks before it publishes |
| Default | Closed: a served machine missing a rule on any event, start or read, or a served train or query with no rule, stops the host | An HTTP endpoint's `Authorize` defaults to false; the resume token is a bearer capability; since 3.8 the server needs bootstrap credentials to start | Not applicable |
| Instances per owner | Yes | Not covered here | Not applicable |

## Concurrency and delivery

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| Two writers on one instance | Optimistic: a concurrency token on the snapshot row; a conflict is a typed result | A distributed lock per instance (a file lock by default, which is single-machine) | Pessimistic row lock by default with EF Core; optimistic is optional |
| A retried request | The first answer is replayed from the instance's recent request ids | Not covered here | The inbox removes duplicates by message id |
| Irreversible side effects | `RunsOnce<TEffect>()`: recorded once, called again only under the same key after a crash | Yours | Yours: consumers must be idempotent |
| Outbox | Only for invoked trains: the snapshot and the queued run commit together | An opt-in outbox for dispatching workflows, at least once | Transactional outboxes on the bus and the consumer, with an inbox |
| Message broker | Not for machines. Optional RabbitMQ for broadcasting run events, SQS for running jobs on workers | Optional, through MassTransit | Required: RabbitMQ, Azure Service Bus, SQS, ActiveMQ, SQL, Kafka and more |

## Storage and operations

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| Databases | PostgreSQL; SQLite for a single process; in-memory for tests | SQL Server, PostgreSQL, SQLite, MySQL, MongoDB and more | About ten saga stores, from EF Core to DynamoDB and Cosmos |
| Run history | Every train run with input, output, timing, failure, each step and each decision; a machine's transitions | An execution journal per instance | The saga row holds the current state |
| In the browser | The same machine, through `@traxsharp/state-machine`; the server refuses a send whose prediction differs from its own result | No | No |
| UI | The operations console | Elsa Studio, for design and runtime | A dashboard, in preview in 9.2 |
| Tracing | No OpenTelemetry spans for runs or transitions; meters through `System.Diagnostics` | OpenTelemetry spans and metrics | OpenTelemetry spans and metrics across message hops |
| Licence | MIT | MIT, with paid extras | 9 and later: commercial. 8: open source |

## Scheduling and queued work

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| Schedules | Cron with seconds, intervals, one-off delays | Cron, Timer, Delay, StartAt | Recurring messages through Quartz or Hangfire; cron for job consumers |
| Exclusions | Days, dates, date ranges and daily time windows, per schedule | Not covered here | Not covered here |
| Missed runs | Fire once now, or do nothing, per schedule | Only `StartAt` catches up | Not covered here |
| Work after other work | Dependent schedules run after a parent succeeds; dormant ones wait for the parent to start them with its own input | Dispatch a workflow from a workflow | Your consumer publishes the next message |
| Limits | A global cap and a cap per group of schedules; priority 0 to 31 | Activation strategies; worker counts | Concurrency limits per job type; a partitioner per key |
| One at a time per subject | Queued runs that share a subject key run one after another | One running instance per correlation id | `UsePartitioner`, on one bus instance |
| Retries | Per schedule, with backoff, resumed from the last checkpoint | Quartz retries; incident strategies | Retry and redelivery middleware |
| Out of retries | A dead letter an operator requeues or acknowledges, in the console or over GraphQL | Not covered here | `_error` and `_skipped` queues, `Fault<T>` messages |
| Workers | The same trains over HTTP, SQS or AWS Lambda, with signed requests; a dead worker's runs are recovered | A distributed runtime with a shared lock; Quartz clustering | Any transport |

## Serving it

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| An API for plain work | Each train is a typed GraphQL mutation that runs or queues it; your EF entities become filtered, paged queries | A REST management API; HTTP endpoint activities | None |
| Only known requests | Persisted operations: the server runs only the GraphQL documents it registered | Not covered here | Not applicable |
| Clients | `trax client` writes a typed TypeScript or React client from your `.graphql` files, or a .NET one | `Elsa.Api.Client`, .NET only | None |
| Live updates | Run and machine subscriptions; run events over SignalR and RabbitMQ | SignalR, for Studio | A SignalR backplane |
| Who may call it | Every exposed train and query needs `[TraxAuthorize]` or `[TraxAllowAnonymous]`, or the host does not start | An `Authorize` flag per endpoint; resource permissions | Your API |
| Audit | Every API call through an audit sink with secrets redacted; `[TraxSensitive]` masks stored inputs and outputs | Not covered here | Not covered here |
| Operators | The web console, and `trax-console` in a terminal, with a JSON lines mode for scripts | Elsa Studio | A dashboard, in preview |

## Testing and tooling

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| Testing | Junctions as plain classes; trains on an in-memory provider; whole-host tests | `ActivityTestFixture`, `WorkflowTestFixture` | `ITestHarness` and a saga harness |
| Architecture guards | Test packages that fail the build when trains, registrations or schemas drift from Trax's shape | Not covered here | Not covered here |
| Templates | `trax-api`, `trax-scheduler`, `trax-hub` | `elsa-server`, `elsa-studio`, `elsa-combined` | Not covered here |
| CLI | `trax generate` trains from OpenAPI or GraphQL; `trax machine` and `trax client` generate code and check it | Not covered here | Not covered here |

## AI agents

| | Trax | Elsa 3 | MassTransit |
|---|---|---|---|
| An agent as a client | The same API and rules as a person; a move that needs evidence is rerun by a train on the server ([Board](/docs/samples/board)) | Not covered here | Not applicable |
| Models inside the work | Model-backed deciders, each answer recorded and replayed on a retry | `Elsa.Agents` on Semantic Kernel; a Studio assistant | Not covered here |
| Coding agents | Agent skills published with each release; `llms.txt` | `llms.txt` | Coding agent skills; `llms.txt` |

## When to choose Elsa

- People who do not write code need to build or change workflows in a designer.
- You want to change workflow definitions at runtime, without a deploy.
- You want ready-made HTTP, email and messaging activities, or expressions in JavaScript, Python or Liquid.
- Your database is SQL Server, MySQL or MongoDB.
- You need OpenTelemetry spans for every workflow and activity today.

## When to choose MassTransit

- The system is message-driven across services, and the state machine reacts to events from other services rather
  than to users.
- You need a choice of broker, and a general transactional outbox and inbox.
- You need routing slips that compensate across services automatically.
- You need tracing across message hops.

## When to choose Trax

- The instance is driven by people or agents through an API, and the server has to check each event against the
  caller and the input.
- You want the page to predict the next state with the same machine the server runs.
- You want the work as plain typed C# steps whose wiring is checked when you build and when the host starts.
- Some steps are decided by a model, and each answer should be recorded, held to a confidence floor and replayed on a
  retry.
- You need a full scheduler in the same packages: exclusions, misfire rules, dependent and dormant schedules, group
  caps, priority, and dead letters an operator requeues.
- The same work has to run on HTTP workers, SQS or Lambda without a message bus.
- The frontend should get a typed client generated from its own queries, and the server should refuse any query it did
  not register.
- You want every run recorded in your own Postgres, and no broker between a caller and the machine.

## Using them together

A junction can publish a MassTransit message, so a Trax train can start work in a message-driven system. A Trax machine
can sit in front of it, deciding which caller may start what, while MassTransit carries the messages behind it.
