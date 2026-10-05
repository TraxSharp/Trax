# EnergyHub: a hub and distributed workers

One process schedules work and serves the API; separate worker processes run it. They share
PostgreSQL (the `background_job` table) and RabbitMQ (lifecycle events), and nothing else.

What it proves:

- The hub runs no scheduled or queued job. `OverrideSubmitter` registers `PostgresJobSubmitter`
  alone, so no local worker starts; a worker claims each job with `FOR UPDATE SKIP LOCKED`.
- A train a worker runs reaches a GraphQL subscriber on the hub, over RabbitMQ. The hub and the
  worker publish on an exchange of their own, `energyhub.lifecycle`: every process bound to an
  exchange receives every event on it, so two applications on one broker need two exchanges.
- Mutations and the `operations` namespace require the Operator role; the solar query is anonymous.
  The only Operator credential is a demo key that exists in Development alone, and the dashboard
  is served only in Development.

| Project | Role |
|---|---|
| `Trax.Samples.EnergyHub` | The trains and the scheduler topology's names |
| `Trax.Samples.EnergyHub.Hub` | GraphQL API, scheduler, dashboard; executes nothing it queues |
| `Trax.Samples.EnergyHub.Worker` | `AddTraxWorker`: polls `background_job` and runs trains |

## Run

From `Trax.Samples/`:

```bash
docker compose up -d       # Postgres 5432 (database trax_energyhub), RabbitMQ 5672 (user trax / trax123)
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub      # terminal 1
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker   # terminal 2
```

Dashboard: <http://localhost:5202/trax>. GraphQL IDE: <http://localhost:5202/trax/graphql>.

## Try it

```bash
# A live read the hub answers itself (anonymous)
curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"{ discover { solar { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { arrayId totalKwh efficiency } } } }"}'

# Queue a grid trade; the worker's console logs it, the hub's does not
curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -H "X-Api-Key: energyhub-operator-key-do-not-use-in-production" \
  -d '{"query":"mutation { dispatch { tradeGridEnergy(input: {ratePerKwh: 0.14, maxSellPercent: 80}) { externalId workQueueId } } }"}'
```

The same commands are in the hub's `Program.cs` header, and `DocumentedExamplesTests` runs them.

## Tests

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.EnergyHub.E2E
```

The suite starts the hub and a worker in one test process against `energyhub_e2e_tests`.
`TRAX_TEST_RABBITMQ` overrides the broker URI (default `amqp://trax:trax123@localhost:5672`).

Docs: <https://traxsharp.net/docs/samples/energy-hub>
