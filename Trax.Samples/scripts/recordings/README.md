# Sample recordings

traxsharp.net's landing page lets a reader try five samples in the browser without running anything: crash a
Recovery run, drive the checkout state machine, chat over WebSockets, watch SignalR events arrive, and call a
persisted-operations API. Each tab plays recordings of the real sample, and these scripts make them. Each one drives
its sample's running host over the same requests the sample's own page or client sends, and writes down every
request, response and pushed event, with the time it arrived. Nothing the website shows is made up; where a sample
takes free input, the recorder records a few presets instead. A sixth script records the operations dashboard that
traxsharp.net/dashboard serves; see [The dashboard](#the-dashboard).

```bash
cd scripts/recordings
npm ci
```

Start one sample's host as its README says (fresh data gives clean ids; its web client is not needed), then run its
script:

| Sample | Script | Host it expects | What it records |
|---|---|---|---|
| Recovery | `node recovery.mjs` | `RECOVERY_HOST`, default `localhost:5260` | 20 runs: two research topics and three refund orders, clean and crashing, and each fork a crashing run can take, each ending with a requeue |
| StateMachine | `node state-machine.mjs` | `STATE_MACHINE_HOST`, default `localhost:5280` | every button the page offers from every state it can reach, for both machines |
| ChatService | `node chat-service.mjs` | `CHAT_HOST`, default `localhost:5210` | Alice and Bob in one room, a small tree of lines to pick from, a listener who is refused, and Charlie added at every point of the conversation |
| SignalRBroadcaster | `node signalr-broadcaster.mjs` | `SIGNALR_HOST`, default `localhost:5270` | the refused connection, sign-in, every kind of ping and the events the hub pushes, sign-out |
| PersistedOperations | `node persisted-operations.mjs` | `PERSISTED_OPERATIONS_HOST`, default `localhost:5240` | every call and management action in each state the store can be in |
| The dashboard | `node dashboard.mjs` | starts Scheduling, Recovery and PersistedOperations itself | every page, filter, row and write of the operations dashboard, from all three hosts |

Each script writes its raw recording under `out/` (gitignored). Add `--copy-to ../../../Trax.Website/src/data` to
write the website's copy as well, with a Trax.Website checkout next to this repository: one compact
`<sample>-recordings.json` with the C# the page shows embedded. `--copy-only` skips recording and rewrites that file
from `out/`. Each script's header comment says exactly what it records and what, if anything, the website copy
changes (ids renumbered, keys masked) and why.

Re-record a sample when its code, its page or what its host sends changes, then run the website's `npm test`, which
plays every recording through the landing page's players.

## The dashboard

`node dashboard.mjs` records what the operations dashboard (Trax.Website's `dashboard/`) needs to run with no API
behind it. It differs from the scripts above in three ways. It starts the hosts itself: Scheduling, Recovery and
PersistedOperations, each on a private port (5391 to 5393) and a database it creates fresh, so it needs only Docker's
Postgres (`docker compose up -d database`; set `TRAX_PG_PORT` when it is not on 5432, and `TRAX_PG_CONTAINER` when
the container is not `trax_database`). It reads the queries, mutations and subscriptions it sends from the
dashboard's own source (`--dashboard`, default `../Trax.Website/dashboard` beside this repository), so it records
exactly what the dashboard asks. And it records a snapshot rather than a sequence: about three minutes of the hosts
running and being driven (with every live event timed), then both schedulers paused, then every list the dashboard
shows walked to its end once per filter, sort and scope, every row's page read, and every write the dashboard offers
sent to a real host. It takes about four minutes.

```bash
TRAX_PG_PORT=5433 node dashboard.mjs --copy-to ../../../Trax.Website/dashboard/src/demo/data
```

The website's copy combines the three hosts as one cluster on one database would show them; the script's header says
how, and what it changes (ids renumbered, the machine's name and paths replaced). After re-recording, run the
dashboard's `npm test` in Trax.Website: its strict check opens every page, filter and row of the demo and fails on any
query the recording does not answer.
