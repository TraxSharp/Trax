# State Machine web frontend

A small React app that drives the sample's two machines over the four generic `stateMachine` mutations. It
shows the plan's frontend design in practice: one machine-agnostic transport, a hook that owns the snapshot,
and machine-specific UI on top.

| File | Role |
|---|---|
| `src/traxTransport.ts` | Machine-agnostic GraphQL client for `save` / `advance` / `load` / `send` (+ `listMachines`). Knows no machine. |
| `src/useMachine.ts` | Hook that resumes a draft on mount, drives it, and re-renders on every change including a rejection. |
| `src/App.tsx` | The UI: a user switcher, a turnstile widget, a checkout wizard, and a live snapshot panel. |

## Run it

Start the backend first (it must be on `http://localhost:5220`, which this app expects):

```bash
cd Trax.Samples && docker compose up -d
./pack-local.sh
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

- **Turnstile**: insert a coin, then push. Push while locked is rejected and the reason shows in the panel.
- **Checkout**: add items, go to Review, then Pay. The state advances to Paid with a receipt. Hit
  **Charge again** and the same receipt comes back, no second charge, which is the exactly-once effect.
- **Switch user** (Alice / Bob): each user sees their own draft for the same instance id, because the server
  scopes drafts to the caller.
