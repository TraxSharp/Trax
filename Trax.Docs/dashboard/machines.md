---
title: Machines
description: "Reference for the operations console's Machines pages: every state machine, a machine's diagram and instances, and one instance's state and runs."
type: reference
area: dashboard
nav_order: 6
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/StateMachinesPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/MachinePage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/StateMachineInstancePage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/MachineDiagram.tsx
---

# Machines

The [state machines](/docs/statemachine) the host registers and their persisted instances,
system-owned and users' drafts alike. The pages never show an instance's context, which is untyped
JSON nothing could mask, or which user owns a draft. Operators do not send machine commands from
the console: an instance's page lists the commands it accepts, and sends none.

An instance is **stuck** when its timer should have fired and has not, or when the run its state
waits on failed or was cancelled.

## The list

`/machines`. Every machine the host registers, from
[`operations.machines`](/docs/sdk-reference/graphql-api/queries/machine-instances#machines), whether
or not it has an instance.

| Column | Value |
|---|---|
| **Machine** | Its name, linking its page |
| **Version** | The machine's version |
| **Instances are held by** | The system, or users and the system |
| **States** | How many states it declares; a dash when it has no diagram |
| **Instances**, **Stuck** | How many instances it has, and how many are stuck; the stuck count opens the machine's page filtered to them |

## A machine

`/machines/{machine}`. Its version, who holds its instances, and its instance and stuck totals.

**States** draws the machine: each state a node badged with how many instances are in it and how
many of those are stuck, tinted for "has instances", "has stuck instances" or "no instances". A
solid edge is a command, a dashed one a timer, and an edge marked "run done", "run failed" or "run
cancelled" is taken when the run its state started ends that way. Under the diagram, each state that
runs a train or starts another machine links it. Clicking a state lists the instances in it;
clicking it again lists them all. Instances in states the current version does not declare, written
under an earlier version, are counted under the diagram, each a filter.

A machine whose rules are written as code the host cannot describe has no diagram; the page says so,
with what the host reports and a link to [declared rules and code rules](/docs/statemachine/delegates-and-declarative-rules).

**Instances** lists them newest first by last write, 20 a page, filtered by **State**, **Owner**
(System or User) and **Stuck only**, each in the address (`state`, `owner`, `stuck`).

| Column | Value |
|---|---|
| **Id** | The instance's id, linking its page |
| **Owner** | System or User |
| **State**, **Last changed** | Where it is and since when |
| **Timer due** | When its timer fires, and **Overdue** once that time has passed |
| **Waiting on** | The run its state invoked, with that run's state, or its queue entry while it is still queued |
| **Attention** | **Stuck** |

The total counts at most 10,000 instances, and the pager stops there; the counts on the diagram are
exact.

## An instance

`/machines/{machine}/system/{id}` for a system-owned instance, `/machines/{machine}/user/{id}?row={row}`
for a user's draft, read through [`operations.machineInstance`](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstance).
The page follows the instance live through `onMachineInstanceChanged`, so its state moves as the
instance does, and polls only while that feed is not connected.

| Part | Shows |
|---|---|
| **Where it is** | The machine's diagram with the instance's state lit; its state, when it last changed, its timer (and **Overdue**), the run it waits on, and whether it is stuck and why |
| **Commands it accepts** | Each command that moves it on from its state, the state it moves to, and its guard. Then the timers, each with its delay, its target state and when it is due, and the run outcomes that move it. |
| **History** | Every transition, newest first: what moved it (a command, a timer, a run's outcome, a save or its start), the states it moved between, who did it (its owner, host code or the machine), when, how long it had been in the state before, and the run or child machine it started, each linked. **Load older** pages back. |
| **Details** | Machine, id, owner, row, machine version, created and updated |
| **Invoked runs** | The runs it invoked, newest first, up to 50: id, train, state (the one its state waits on marked **Live**), times, failure class, external id. A user's draft lists only the run its state waits on. While that run is still queued, its queue entry is linked. |

A deleted or expired instance keeps showing how it was last seen, with a note saying so.

**Cancel**, on a system-owned instance whose state waits on a run, cancels that run after a
confirmation: a queued run never starts, a running one stops at its next junction, and the instance
moves through the state's `OnCancelled` edge. It calls
[`cancelMachineInstance`](/docs/sdk-reference/graphql-api/mutations/machine-instances#cancelmachineinstance).
A draft offers no Cancel.

The history is kept from the upgrade that added it
([089](/docs/reference/schema-migrations#state-machine-instance-history-089-sqlite-048)); an instance
written before then starts its history at its next transition. It is deleted with the instance, and
it never holds the instance's context or which user sent a command.
