# Trax Recovery Sample

A live demo of a crashed run retrying without paying for its model's answers twice. A train asks a
decider (a stand-in model) which track to take, and Trax records each answer with a hash of the state
it was about. A later step crashes, and the manifest's automatic retry reuses the recorded answers
while that hash still matches, so it takes the same tracks. Every junction, question and track
reaches the page as it happens, through junction events.

![The Recovery demo: a refund's payment step crashes, and the retry reuses the model's recorded answer](screenshot.png)

It proves three Trax features working together: [train decisions](https://traxsharp.net/docs/core/decisions),
[retries that replay decisions](https://traxsharp.net/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions)
and [junction events](https://traxsharp.net/docs/effect/junction-events). The full walkthrough is on
[traxsharp.net/docs/samples/recovery](https://traxsharp.net/docs/samples/recovery).

```
Recovery/
├── Trax.Samples.Recovery/          Trains, junctions, the demo decider, the fault injector
├── Trax.Samples.Recovery.Api/      One host: GraphQL + scheduler + local workers + dashboard
└── Trax.Samples.Recovery.Client/   React 19 + Vite + Apollo + graphql-ws page
```

## Run it

Needs the .NET 10 SDK, Node.js 20 or later, and Docker. From the `Trax.Samples` folder:

```bash
docker compose up -d database          # Postgres 5432, database trax_recovery
dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api
```

In a second terminal:

```bash
cd samples/Recovery/Trax.Samples.Recovery.Client
npm ci
npm run dev
```

Open http://localhost:5173. The host listens on http://localhost:5260; the dashboard is at
http://localhost:5260/trax and the GraphQL IDE at http://localhost:5260/trax/graphql, both in
Development only. When your Postgres is not on 5432, start the host with
`ConnectionStrings__TraxDatabase="Host=localhost;Port=<port>;Database=trax_recovery;Username=trax;Password=trax123"`.

## Try it

The page shows the case the model is asked about, a **Decisions** table (one row per question, one
column per attempt: the answer, where it came from, how long it took, the state hash), the **Route
taken** by each attempt, and the train's source and raw junction events in tabs.

1. Leave **Refund approval**, order **A-1001** and **Crash the first attempt** selected and press
   **Run**. Attempt 1 loads the case, asks the model whether to pay it (`ApproveRefund`), and crashes
   in `IssuePayment`. A few seconds later the manifest retries: attempt 2's answer reads
   **replayed, model not called**, in milliseconds rather than the model's second or so, and the
   retry takes the same `Yes` track.
2. Press **Run** again and, while the retry counts down, press **Add an earlier refund to the order**.
   The case the model sees now shows one more earlier refund, so the retry's state no longer hashes
   the same: the replay is refused, the model is asked again (**asked again: the case changed**) and
   the refund goes to a person (the `Unsure` track) instead of being paid.
3. Press **Run** again and, during the countdown, press **Ask the model again**. The retry starts at
   once and asks afresh (**asked again, on purpose**).
4. Try orders A-1002 and A-1003. The model is unsure about A-1002 and declines A-1003, so their runs
   take the review and decline tracks; the step on that track crashes the same way, and the retry
   reuses the answer and takes the same track.
5. Pick **Research brief** and press **Run**. The train asks two questions (`Source`, `Depth`) and
   crashes while writing the report; the retry reuses both answers.
6. After a run completes, **Re-run, asking afresh** re-queues its last execution with
   `requeueExecution(id, askAfresh: true)`, as a run of its own.

The same over plain GraphQL, with header `X-Api-Key: recovery-operator-key-do-not-use-in-production`:

```graphql
mutation { dispatch { startRun(input: { scenario: REFUND, orderId: "A-1001", crashOnce: true }) {
  output { runId manifestId manifestExternalId } } } }

query { operations { executions(manifestId: 1, order: OLDEST) { items { id trainState failureJunction } } } }

query { operations { junctionRuns(metadataId: 42) { position kind name state answer replayed attempt } } }
```

## Keys

Two demo keys, registered only in Development (a key carrying `do-not-use-in-production` refuses to
start anywhere else):

| Key | Role | Sees |
|---|---|---|
| `recovery-operator-key-do-not-use-in-production` | `Operator` | The operations view: every answer and every step name. The page uses it. |
| `recovery-viewer-key-do-not-use-in-production` | `Viewer` | The broadcast view of the two scenario trains: the run's shape, with answers and the steps on a track withheld. |

## Demo-only settings

The scheduler polls every second and retries after four seconds with no backoff growth, so a retry
is visible within seconds, and records two runs of its own each second; `AddMetadataCleanup` sweeps
those after ten minutes and keeps the scenario runs. `appsettings.Development.json` holds a fixed state hash key. Neither
belongs in production: keep the scheduler defaults, and keep the key with your other secrets.

Case files and armed crashes live in memory. A run started before the host restarts has lost its
case file, so every retry fails and the manifest dead-letters. A re-queue would fail the same way,
so the page offers no re-run once a run is dead.

## Using Nimble

Set `Recovery:Model` to `Nimble` and `Recovery:Nimble:Endpoint` to the full URL of a Nimble server's
`POST /v1/systemone` (one you run; Trax has no default). The demo decider is then not registered.

## Tests

`tests/Trax.Samples.Recovery.E2E` runs the real host against Postgres (database
`recovery_e2e_tests`) with a counting decider:

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.Recovery.E2E
```
