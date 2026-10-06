# ContentShield: ephemeral (Lambda-style) runners

An API that executes nothing itself: every train it dispatches runs on a runner that is an AWS
Lambda function in production and a local Kestrel server in development.

What it proves:

- Queued mutations are POSTed to the runner's `/trax/execute` (`UseRemoteWorkers`) and return at
  once. Every synchronous run, run mutations and `[TraxQuery]` queries alike, is POSTed to
  `/trax/run` (`UseRemoteRun`) and waits for the output. No `background_job` row is written.
- Every request to the runner is signed with a key both sides share; the runner refuses anything
  unsigned with `401`.
- A train the runner finishes reaches a GraphQL subscriber on the API, over RabbitMQ. Only the
  Moderator trains are `[TraxBroadcast]`: a broadcast train's events carry every run's output to
  every subscriber the train admits, so the anonymous review and lookup are not. The API and
  the runner publish on an exchange of their own, `contentshield.lifecycle`, so another Trax
  application on the same broker does not receive their events.
- Anyone may submit content and look up a result; reports and violation notices need the Moderator
  role, whose only key is a demo key that exists in Development alone. The dashboard is
  Development-only.

| Project | Role |
|---|---|
| `Trax.Samples.ContentShield` | The trains, the roles, the signing-key helper |
| `Trax.Samples.ContentShield.Api` | GraphQL API, dispatch (`UseRemoteWorkers` + `UseRemoteRun`), dashboard |
| `Trax.Samples.ContentShield.Runner` | A `TraxLambdaFunction`; `Program.cs` serves it locally with `RunLocalAsync` |

## Run

From `Trax.Samples/`:

```bash
docker compose up -d       # Postgres 5432 (database trax_contentshield), RabbitMQ 5672 (user trax / trax123)
dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner   # terminal 1, port 5205
dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Api      # terminal 2, port 5204
```

Both start in Development (`Properties/launchSettings.json`) and sign with a published demo key.
Anywhere else set `Trax__RunnerSigningKey` to the same `openssl rand -base64 32` value on both, or
the API refuses to start.

## Try it

```bash
# Queue a review; the runner's console logs it
curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"mutation { dispatch { moderation { reviewContent(input: {contentId: \"test-002\", contentType: \"video\", contentBody: \"suspicious video content\"}) { externalId workQueueId } } } }"}'

# Run a report on the runner and wait for it (Moderator key)
curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
  -H "X-Api-Key: contentshield-moderator-key-do-not-use-in-production" \
  -d '{"query":"mutation { dispatch { reports { generateModerationReport(input: {reportPeriod: \"Daily\"}) { externalId output { totalReviewed totalFlagged } } } } }"}'
```

The API's `Program.cs` header holds a third example, and `DocumentedExamplesTests` runs all three.

## Tests

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.ContentShield.E2E
```

No AWS needed: the suite serves the real `Function` through `RunLocalAsync` on a free port and
points the API at it, against `contentshield_e2e_tests`. `TRAX_TEST_RABBITMQ` overrides the broker
URI.

Docs: <https://traxsharp.net/docs/samples/content-shield>
