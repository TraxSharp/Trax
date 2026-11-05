---
title: Trax and AI agents
description: "Where Trax sits around an AI agent, and the Board sample, where an agent moves a ticket only when the server reruns its evidence."
type: explanation
area: general
nav_order: 3
---

# Trax and AI agents

An AI agent is one more client of a process, like a person in a browser or a script. It is the one most likely to
try a move it should not make, and the one least likely to follow rules written only in its prompt. This page
explains where Trax sits around an agent. It assumes [Trax in five minutes](/docs/introduction).

## Every client gets the same rules

![One workflow, defined once on the server, driven by four kinds of client. Web users get steps in order and valid data at each step. Schedules and queues get retries, dead letters and a record of every run. Scripts and services in any language get the same rules. AI agents get only the moves they are allowed, and evidence the server reruns.](/docs-images/many-drivers.svg)

Because the rules live on the server, every kind of client gets the same ones. People in a browser
get the steps in order and their input checked, through the TypeScript client Trax generates.
Scheduled and queued work runs when you say; Trax queues it, retries it as often as you allow,
dead-letters it when it runs out of retries, and records every run. Scripts and services call the
same GraphQL API Trax serves, in whatever language they are written in, and the rules hold for them
too, including the ones their language cannot check. AI agents can only make the moves you
declared, and when a move needs evidence, a check you write as a train reruns it on the server
before Trax accepts the move.

## Where Trax sits around an AI agent

![Two layers around an AI agent. The sandbox lets the agent run code, edit files, call tools and read data, but nothing in it reaches real systems. The only way out is through Trax, which checks that the move is allowed in this state, allowed for this agent, and that its evidence holds up, by rules you write. Only verified moves reach the real systems: tickets, orders, data.](/docs-images/agent-sandbox.svg)

AI agents are the most demanding client. Two layers do two different jobs. A sandbox limits what an
agent can do: it can run code and make a mess, but only in its own box. Trax limits what it can
change: its only way out is a small set of declared moves, each checked against the state, the
agent's role, and evidence the server checks itself rather than takes on the agent's word. The
sandbox is not Trax; you run it with whatever tool you already use. Inside Trax, you write the rules
and the evidence checks, and Trax makes sure the agent cannot get around them. Most agent setups
have the first layer and improvise the second with instructions in a prompt.

## The Board sample

![The Board sample. A bug ticket moves through five lanes, each owned by one client: Triage by triage-bot, Reproduce by repro-bot, Fix by fix-bot, QA by qa-bot and Review by Sam, a person. The ticket is in Fix, held by fix-bot. An agent submits its evidence, a check train the sample's author wrote reruns it on the server, and the ticket moves only if the check accepts it; otherwise it stays put.](/docs-images/board-lanes.svg)

[Board](/docs/samples/board) is a Trax sample where bots move bug tickets through lanes. Four
scripted bots and one person each own a lane, with keys of their own. A bot never moves a ticket
itself: it submits evidence for its lane, a check train on the server reruns that evidence against
the rules, and only the train's verdict moves the ticket. The sample's author wrote one check train
per lane, as ordinary trains; building your own agent workflow, those are the trains you would
write. Trax is what makes the verdict the only way a ticket moves. The last lane belongs to a person, so
human sign-off is just another lane with a role. The bots are scripted, not language models, but
they talk to the server the way an AI agent would.

What an agent cannot do there, and what it gets back:

| The agent tries to | The server answers | Because |
|---|---|---|
| Claim a ticket another agent holds | `ALREADY_CLAIMED` | a claim needs a ticket nobody holds |
| Approve its own fix | `FORBIDDEN` | only a reviewer may approve |
| Close a ticket as not reproducible too early | `GUARD_FAILED` | closing needs at least two cases the server ran |
| Claim work when none is waiting | `NOTHING_TO_CLAIM` | there is no ticket in its lane |

An agent cannot report its own test results either: the cases on record are written only by the
server's check.

## Where to go next

| You want to | Read |
|---|---|
| Run the Board sample | [Board](/docs/samples/board) |
| Decide between a train and a machine for a step | [Train or machine](/docs/statemachine/train-or-machine) |
| Write the rules that say who may send a move | [Authorization rules](/docs/security/authorization-rules) |
| Point a coding agent at these docs | [`/llms.txt`](/llms.txt), one file that lists every page |
