# Trax.Samples.ApiAudit

Minimal demo of [Trax.Api.GraphQL.Audit](../../../../Trax.Api/src/Trax.Api.GraphQL.Audit/). Every GraphQL request is captured as a `TraxAuditEntry`, routed through a bounded channel, and flushed in batches to a console sink.

## Running

```bash
cd Trax && ./pack-local.sh
dotnet run --project Trax.Samples/samples/ApiAudit/Trax.Samples.ApiAudit
```

Issue a request with a demo key:

```bash
curl -H "X-Api-Key: alice-key" -H "Content-Type: application/json" \
     -d '{"query":"{ dispatch { echo(input:{message:\"hi\"}) { output { echoed } } } }"}' \
     http://localhost:5220/trax/graphql
```

Each request produces one line like:

```
[audit] 2026-04-14T12:00:00.000Z principal=alice op=(anon) success=True duration=5ms
```

## Security

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [SECURITY-DISCLAIMER.md](../../../../Trax.Api/SECURITY-DISCLAIMER.md).

The demo keys (`alice-key`, `bob-key`) are plaintext constants for illustration only. Production systems must source keys from a secret manager, enforce HTTPS, and rotate credentials.
