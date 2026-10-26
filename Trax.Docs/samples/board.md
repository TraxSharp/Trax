---
title: Board
description: "The Board sample: scripted agents work bug tickets through a board as clients, and the server reruns their evidence before a ticket moves."
type: explanation
area: samples
nav_order: 3
---

# Board

`samples/Board` is a project board for bug tickets, worked by clients the server does not trust. Four scripted
agents (triage-bot, repro-bot, fix-bot, qa-bot) and one person (Sam, who reviews) each have a key of their own and
one lane: Triage, Reproduce, Fix, QA and Review. The project with the bugs is a shop's shipping rules, as data, and
every ticket is a report that the rules charge an order something the shop's policy says they should not.

An agent never moves a ticket. It claims one in its lane, gets a work package back, and sends its evidence (a case,
a patch, a sweep of subtotals) on an assignment of its own. The server runs that evidence against the rules itself,
and only if it holds does the ticket move to the next lane.

## What it proves

| Feature | Where |
|---|---|
| A system-owned machine that no client reads or moves: `TicketMachine`, advanced only by host code through `IMachineInstances` | `Machines/TicketMachine.cs`, `AuthorityTests` |
| A served machine per worker, whose events are each gated by the lane's role (`Requires`) and whose checking states run the lane's check train | `Machines/AssignmentMachine.cs`, `ScenarioTests` |
| Check trains on a user-owned machine: authorized as the worker who entered the state, the owner read through `InvokedRun`, the outcome routed by a guarded `OnDone` | `Trains/Checks/`, `ScenarioTests` |
| A claim held once: the claim is a transition of the ticket guarded on nobody holding it, so a claim on a held ticket is refused (`ALREADY_CLAIMED`) | `Trains/Work/ClaimWorkTrain.cs`, `Machines/TicketMachine.cs` |
| Evidence the server reruns rather than trusts: a case whose expected charge is not the policy's, a patch that breaks an earlier case, and evidence on a ticket the worker never claimed are all refused | `Trains/Checks/`, `AuthorityTests`, `ScenarioTests` |
| Agents on the generated client: `trax client` from the host's manifest, the assignment's twin and server, and the claim and board as train functions | `Trax.Samples.Board.Agents/src` |

## Run

From the `Trax.Samples` root:

```bash
docker compose up -d                                         # Postgres on localhost:5432, database trax_board
dotnet run --project samples/Board/Trax.Samples.Board.Api    # Development, http://localhost:5290

# In a second terminal: the agents, every scenario
cd samples/Board/Trax.Samples.Board.Agents && npm ci && npm run scenario

# The board itself, read once a second, at http://localhost:5173
cd samples/Board/Trax.Samples.Board.Client && npm ci && npm run dev
```

The scenarios run against a fresh database. On one that already holds their tickets, pass `--suffix -2` to file them
under new ids (`npm run scenario -- --suffix -2`), and name one to run only that (`npm run scenario -- skip-ahead`).
Each scenario shows one thing:

1. **A ticket goes through every lane** (`every-lane`). An order of exactly $50 pays shipping, and the policy says
   it ships free. Each lane's client claims the ticket and sends its evidence, the server checks it, and the ticket
   moves on. Nothing is refused.
2. **An agent tries to skip ahead** (`skip-ahead`). fix-bot claims the ticket before it is in Fix
   (`NOTHING_TO_CLAIM`), and later tries to approve its own fix (`FORBIDDEN`). The ticket carries on as normal.
3. **A fix that breaks an earlier case** (`fix-regresses`). fix-bot's first patch says free from $6.00, a typo for
   $60. It fixes the reported order, but a $20.00 case an earlier fix was checked against would now ship free, so the
   server refuses it. The second patch passes.
4. **QA finds what the fix missed** (`qa-catches-edge`). A patch of free above $99.00 passes the repro case and every
   earlier one. qa-bot's sweep finds $99.50, which the patched rules would ship free, and the ticket goes back to Fix.
5. **A bug that does not reproduce** (`cannot-reproduce`). The rules already match the policy. repro-bot may close
   the ticket only once the server has run two of its cases: a close after one is refused (`GUARD_FAILED`).

Each move in `src/scenarios.ts` names the answer it expects from the server, and a run stops at the first answer
that differs, so the captions the website shows with the recording never describe something the server did not do.

## The two machines

The ticket is the board. `TicketMachine` declares `SystemOwned()`, so no client operation reaches it, and its states
are the lanes. A claim is one of its transitions, guarded on nobody holding the ticket:

```csharp from=Trax.Samples/samples/Board/Trax.Samples.Board/Machines/TicketMachine.cs
            .On(TicketTrigger.CannotReproduce)
            .WithInput<NoteInput>()
            .When(Field((TicketContext c) => c.FailedCases).AtLeast(2))
            .Because("A ticket is closed only after at least two cases did not reproduce it.")
            .Reduce(Moved())
            .To(TicketState.Closed);
```

The assignment is what a worker drives. `AssignmentMachine` is `[TraxMachine]` and user-owned, so each worker owns
its own instances, and the host serves it as `machines { assignment { ... } }`. Each event delivers one lane's
evidence and names the role that lane needs:

```csharp from=Trax.Samples/samples/Board/Trax.Samples.Board/Machines/AssignmentMachine.cs
            .On(AssignmentTrigger.Close)
            .Requires(BoardRoles.Reproducer)
            .When(Field((AssignmentContext c) => c.FailedCases).AtLeast(2))
            .Because("Close needs at least two cases the server ran that did not reproduce it.")
            .To(AssignmentState.CheckingClose)
```

`FailedCases` is written only by a check's outcome, so the agent cannot claim cases it never had run. The state each
event enters runs the lane's check train, and only the train's outcome leaves it:

```csharp from=Trax.Samples/samples/Board/Trax.Samples.Board/Machines/AssignmentMachine.cs
        m.In(state)
            .Context<AssignmentContext>()
            .Invokes<TTrain, TInput, CheckResult>(input)
            .OnDone(
                state == AssignmentState.CheckingClose
                    ? AssignmentState.Closed
                    : AssignmentState.Accepted,
                when: Accepted,
                reduce: TakeVerdict
            )
            .OnDone(AssignmentState.Rejected, reduce: TakeVerdict)
            .OnFailed(AssignmentState.Rejected, reduce: CheckDidNotFinish)
            .OnCancelled(AssignmentState.Rejected, reduce: CheckDidNotFinish);
```

A check train checks that the worker holds the ticket in the lane (from `InvokedRun.UserKey`, never from anything it
sent), runs the evidence against the shop's rules and its policy, and only then fires the ticket's transition. Each
`[TraxAuthorize(Roles = ...)]` matches the event that enters its state, which the host checks at startup.

## The agents

`Trax.Samples.Board.Agents` is a Node project on the client `trax client` generated from the host's manifest, as is
the board page, `Trax.Samples.Board.Client`. After changing a machine, a train or a document, write both again:

```bash
# from the repository root; the trax CLI built from source takes the engine bundled from Trax.StateMachine/ts
node Trax.StateMachine/ts/tools/bundle-cli.mjs --out /tmp/trax-engine
(cd Trax.Samples/samples/Board/Trax.Samples.Board.Api && dotnet run -- --trax-export-client /tmp/board-manifest.json)
for client in Trax.Samples.Board.Agents Trax.Samples.Board.Client; do
  (cd Trax.Samples/samples/Board/$client && dotnet run --project ../../../../Trax.Cli/src/Trax.Cli -- client \
    --manifest /tmp/board-manifest.json --out src/trax --documents 'src/graphql/*.graphql' --tools-dir /tmp/trax-engine)
done
```

The claim, the board and the charge tool are train functions from the agents' own documents (`src/graphql`). The
assignment is driven through its twin and server with `ServerMachineController`, which sends each event by its
persisted id and follows the assignment's push until the check answers. The generated client keeps one connection
per process, so the scripted runner signs each worker in before it acts; agents run in processes of their own need
nothing of the sort.

The agents are scripted: what each sends is written in `src/scenarios.ts`. Everything they send goes to the real
host, and every answer, refusal and verdict is the host's.

## Tests

`Trax.Samples.Board.E2E` drives each scenario as the agents do and asserts every refusal and check, and
`AuthorityTests` covers what a client cannot do: claim from another lane, send evidence on a ticket it did not claim,
read another worker's assignment, or reach the ticket machine at all.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances) | [InvokedRun](/docs/sdk-reference/statemachine-api/invoked-run) | [Invoking a train](/docs/statemachine/invoking-trains) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package) | [trax client](/docs/reference/cli#frontend-clients-trax-client) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth)
