# State Machine web frontend

A small React app that drives the sample's two machines through the client `trax client` generated from the host.
Every operation is sent by the persisted id the host registered, with its values as variables; the host refuses a
document sent as text. Each machine is started with `start`, which mints the instance's id on the server, and moved
only by its events.

| File | Role |
|---|---|
| `src/trax/` | The generated client: each machine's twin, its documents by id and its hooks, the train functions (`paymentsListCharges`), their types, and the manifest it was generated from. Do not edit; regenerate it (below). |
| `src/connection.ts` | Points the generated client at the host as the signed-in user, and logs every request for the Inspector, with the document its id names read from the manifest. |
| `src/useMachineStage.ts` | The instance this browser keeps for a user and machine, through the generated `useRemembered<Machine>` hook, and the last request for the walkthrough. |
| `src/machines.ts` | How the page describes the two machines: states, the rule each must hold, moves, guards and the effect. Display only; the server decides. |
| `src/App.tsx` | The page: the sidebar, one tab per machine, the triggers and the cart's `AddItem`. |
| `src/Diagram.tsx` | A machine's states and moves: the current state glows, the moves out of it are marked, and the last move lights up. |
| `src/Walkthrough.tsx` | **What the server just did**: the server's checks for the last request, in order, and where it stopped. |
| `src/SideEffect.tsx` | **Side effect: the charge**: the binding, the request the page sends to pay, and the payment provider's charges. |
| `src/Inspector.tsx`, `src/inspectorLog.ts` | **Show the raw requests**: every request the page sends (an id and variables) and what came back. |

## Run it

Start the backend first (it must be on `http://localhost:5280`, which this app expects):

```bash
cd Trax.Samples && docker compose up -d
dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api
```

Then the frontend:

```bash
cd samples/StateMachine/web
npm install
npm run dev
```

Open http://localhost:5173.

## Regenerate the client

The page calls two trains, each through a document of its own in `src/graphql/`; the host registers them from
`src/trax/trax.documents.json`, which its build copies beside it. After changing a machine, a train or a document,
export the host's manifest and regenerate (`trax` is the Trax.Cli tool):

```bash
(cd samples/StateMachine/Trax.Samples.StateMachine.Api && dotnet run -- --trax-export-client ../web/src/trax/manifest.json)
cd samples/StateMachine/web
trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/graphql/*.graphql'
```

CI exports the manifest again and runs `trax client --check` with the same documents, so a client out of step with
the host fails the build.

## What to try

- **A simple machine (turnstile)**: fire **Coin** with a quarter, then **Push**. Fire **Push** while Locked and the
  server answers `NO_TRANSITION`: there is no Push arrow out of Locked. A penny is refused by the Coin move's guard
  (`GUARD_FAILED`). The walkthrough shows each check and where the server stopped.
- **A machine with an effect (checkout)**: add two items, fire **Next**, then **Pay**. Each `AddItem` sends only the
  item's name; the server computes the total. The walkthrough shows the page sent only the draft's id to pay, and the
  payment provider lists one charge for the server's total. Press **Pay** again: the same receipt comes back and the
  provider still lists one charge.
- **Next on an empty cart**: refused by the move's guard (`GUARD_FAILED`); nothing is stored.
- **Switch user** (Alice / Bob): each user has their own draft of each machine, and their own charges.
