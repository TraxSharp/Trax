# State Machine web frontend

A small React app that drives the sample's two machines through their typed operations under `machines { ... }`.
It starts each machine with `start`, which mints the instance's id on the server, and moves it only with the
machine's events, one mutation each: one transport, a hook that owns the snapshot, and machine-specific UI on top.

| File | Role |
|---|---|
| `src/traxTransport.ts` | GraphQL client for `start` / `advance` / `load` / `send` / `discard` (+ `listMachines`, `listCharges`), writing each machine's typed documents: an event's field, its input, and the context fields to read back. |
| `src/useMachine.ts` | Hook that resumes the user's instance on mount or starts one (keeping the minted id in the browser), drives it, re-renders on every change including a rejection, and remembers the last request for the walkthrough. |
| `src/machines.ts` | How the page describes the two machines: states, the rule each must hold, moves, guards and the effect. Display only; the server decides. |
| `src/App.tsx` | The page: the sidebar, one tab per machine, the triggers and the cart's `AddItem`. |
| `src/Diagram.tsx` | A machine's states and moves: the current state glows, the moves out of it are marked, and the last move lights up. |
| `src/Walkthrough.tsx` | **What the server just did**: the server's checks for the last request, in order, and where it stopped. |
| `src/SideEffect.tsx` | **Side effect: the charge**: the binding, the request the page sends to pay, and the payment provider's charges. |
| `src/Inspector.tsx`, `src/inspectorLog.ts` | **Show the raw requests**: every GraphQL request the page sends and what came back. |

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

## What to try

- **A simple machine (turnstile)**: fire **Coin** with a quarter, then **Push**. Fire **Push** while Locked and the
  server answers `no-transition`: there is no Push arrow out of Locked. A penny is refused by the Coin move's guard
  (`guard-failed`). The walkthrough shows each check and where the server stopped.
- **A machine with an effect (checkout)**: add two items, fire **Next**, then **Pay**. Each `AddItem` sends only the
  item's name; the server computes the total. The walkthrough shows the page sent only the draft's id to pay, and the
  payment provider lists one charge for the server's total. Press **Pay** again: the same receipt comes back and the
  provider still lists one charge.
- **Next on an empty cart**: refused by the move's guard (`guard-failed`); nothing is stored.
- **Switch user** (Alice / Bob): each user has their own draft of each machine, and their own charges.
