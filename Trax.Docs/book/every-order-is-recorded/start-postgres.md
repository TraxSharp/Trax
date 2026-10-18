---
title: "3.1 Start Postgres"
description: "Chapter 3 of the Trax book, section 1: Start Postgres."
type: tutorial
area: general
parent: "3. Every order is recorded"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/testing-place-order/run-them
---

# 3.1 Start Postgres

Run Postgres in a container, with a database for Shopfront:

```bash
docker run -d --name shopfront-postgres -p 5432:5432 \
  -e POSTGRES_PASSWORD=shopfront -e POSTGRES_DB=shopfront postgres:17
```

Shopfront reads its connection string from configuration. Replace `appsettings.json`:

```json file=appsettings.json
{
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider": "Debug"
    }
  },
  "AllowedHosts": "*"
}
```

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Metadata](/docs/effect/metadata)

---

Next: [3.2 Packages](/docs/book/every-order-is-recorded/packages)
