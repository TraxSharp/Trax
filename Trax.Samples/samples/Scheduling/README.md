# Scheduling

A Trax host that schedules trains and handles the ones that fail: interval and cron manifests,
a one-off, a dependent and a dormant dependent, retries with backoff, a dead letter and its
requeue. The GraphQL operations surface and the dashboard show and steer all of it.

## What it proves

| Manifest | Schedule | Shows |
|---|---|---|
| `refresh-exchange-rates` | `Every.Seconds(5)` | an interval manifest |
| `reprice-catalog` | `ThenInclude` | a dependent: runs after each successful refresh |
| `alert-rate-spike` | `Include(..., o => o.Dormant())` | a dormant dependent: runs only when the refresh activates it |
| `send-daily-digest` | `Cron.Daily(hour: 7)` | a cron manifest, evaluated in UTC |
| `send-launch-announcement` | `ScheduleOnce(..., 10 s)` | a one-off that disables itself after it succeeds |
| `import-supplier-feed` | `Every.Seconds(15)`, `MaxRetries(2)` | a failing train: two retries with backoff, then a dead letter |

The supplier is down for its first three calls after startup, so `import-supplier-feed` fails,
is retried after 2 s and then 4 s, and is dead-lettered. Requeue the dead letter and it runs
again, and succeeds: the outage is over.

`tests/Trax.Samples.Scheduling.E2E` proves each row against the running host.

## Run it

From the `Trax.Samples` folder:

```bash
docker compose up -d database          # Postgres 5432, database trax_scheduling
dotnet run --project samples/Scheduling/Trax.Samples.Scheduling.Host
```

The host listens on `http://localhost:5230`. The polling, retry delay and backoff are set to
demo speed in `Program.cs`, each with its production default beside it.

## Try it

Open the dashboard at http://localhost:5230/trax (Development only). Within about ten seconds
**Data > Dead Letters** shows `import-supplier-feed`, and its three failed runs are under
**Data > Metadata**. What the host and its trains log is stored in Postgres
(`AddDataContextLogging`) and listed under **Data > Logs**.

The same, over GraphQL. Every `operations` field needs the demo operator key, which exists
only in Development. The dead letter's id is `1` on a fresh database; use the id the first
query returns:

```bash
KEY='X-Api-Key: operator-key-do-not-use-in-production'
URL=http://localhost:5230/trax/graphql

curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "{ operations { deadLetters { deadLetters(status: AWAITING_INTERVENTION) { items { id manifestName reason } } } } }"}'
# {"data":{"operations":{"deadLetters":{"deadLetters":{"items":[{"id":1,"manifestName":"...IImportSupplierFeedTrain",
#   "reason":"Max retries exceeded: (3) failures > (2) max retries"}]}}}}}

curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "mutation { operations { deadLetters { requeueDeadLetter(id: 1) { success workQueueId message } } } }"}'
# {"data":{"operations":{"deadLetters":{"requeueDeadLetter":{"success":true,"workQueueId":14,"message":"Dead letter requeued"}}}}}

curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "{ operations { deadLetters { deadLetter(id: 1) { status retryMetadataId } } } }"}'
# {"data":{"operations":{"deadLetters":{"deadLetter":{"status":"RETRIED","retryMetadataId":81}}}}}

curl -s $URL -H 'Content-Type: application/json' -H "$KEY" -d '{"query":
  "mutation { operations { triggerManifest(externalId: \"send-daily-digest\") { success message } } }"}'
# {"data":{"operations":{"triggerManifest":{"success":true,"message":"Manifest triggered"}}}}
```

Without the key, `operations` answers `Not authorized.` with code `TRAX_AUTHORIZATION`.

## Docs

[Scheduling sample](https://traxsharp.net/docs/samples/scheduling) walks through the code. The
features: [Scheduling](https://traxsharp.net/docs/scheduler),
[Dependent Trains](https://traxsharp.net/docs/scheduler/dependent-trains),
[Dead Letters & Cleanup](https://traxsharp.net/docs/scheduler/dead-letters-and-cleanup).
