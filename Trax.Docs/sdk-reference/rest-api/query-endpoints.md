---
layout: default
title: Query Endpoints
parent: REST API
grand_parent: SDK Reference
nav_order: 4
---

# Query Endpoints

Six read-only endpoints for querying manifests, manifest groups, and train executions. All queries hit the database directly via `IDataContextProviderFactory` and return paginated results.

All paths are relative to the route prefix (default: `/api`).

## Pagination

List endpoints accept `skip` and `take` query parameters and return a `PagedResult<T>` wrapper.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `int` | `0` | Number of records to skip |
| `take` | `int` | `25` | Number of records to return |

Response structure:

```json
{
  "items": [],
  "totalCount": 150,
  "skip": 0,
  "take": 25
}
```

| Field | Type | Description |
|-------|------|-------------|
| `items` | `T[]` | The page of results |
| `totalCount` | `int` | Total number of records matching the query (before pagination) |
| `skip` | `int` | The skip value used |
| `take` | `int` | The take value used |

## Manifests

### GET /manifests

Returns all manifests, ordered by ID descending (newest first).

#### Response

`200 OK` — `PagedResult<ManifestSummary>`

```json
{
  "items": [
    {
      "id": 5,
      "externalId": "sync-daily",
      "name": "Trax.Samples.Trains.ISyncTrain",
      "isEnabled": true,
      "scheduleType": "Cron",
      "cronExpression": "0 3 * * *",
      "intervalSeconds": null,
      "maxRetries": 3,
      "timeoutSeconds": null,
      "lastSuccessfulRun": "2026-03-03T03:00:12Z",
      "manifestGroupId": 1,
      "dependsOnManifestId": null,
      "priority": 0
    }
  ],
  "totalCount": 12,
  "skip": 0,
  "take": 25
}
```

#### curl

```bash
curl "http://localhost:5000/api/manifests?skip=0&take=10"
```

### GET /manifests/{id}

Returns a single manifest by database ID.

#### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `id` | path | `long` | Yes | The manifest's database ID |

#### Response

`200 OK` — `ManifestSummary`

`404 Not Found` — if no manifest exists with the given ID.

#### curl

```bash
curl http://localhost:5000/api/manifests/5
```

## Manifest Groups

### GET /manifest-groups

Returns all manifest groups, ordered by ID descending.

#### Response

`200 OK` — `PagedResult<ManifestGroupSummary>`

```json
{
  "items": [
    {
      "id": 1,
      "name": "daily-syncs",
      "maxActiveJobs": 2,
      "priority": 10,
      "isEnabled": true,
      "createdAt": "2026-02-15T10:00:00Z",
      "updatedAt": "2026-03-01T08:30:00Z"
    }
  ],
  "totalCount": 3,
  "skip": 0,
  "take": 25
}
```

#### curl

```bash
curl "http://localhost:5000/api/manifest-groups?skip=0&take=10"
```

### GET /manifest-groups/{id}

Returns a single manifest group by database ID.

#### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `id` | path | `long` | Yes | The manifest group's database ID |

#### Response

`200 OK` — `ManifestGroupSummary`

`404 Not Found` — if no manifest group exists with the given ID.

#### curl

```bash
curl http://localhost:5000/api/manifest-groups/1
```

## Executions

### GET /executions

Returns train execution records (metadata), ordered by start time descending (most recent first).

#### Response

`200 OK` — `PagedResult<ExecutionSummary>`

```json
{
  "items": [
    {
      "id": 42,
      "externalId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "name": "Trax.Samples.Trains.ISyncTrain",
      "trainState": "Completed",
      "startTime": "2026-03-03T03:00:00Z",
      "endTime": "2026-03-03T03:00:12Z",
      "failureStep": null,
      "failureReason": null,
      "manifestId": 5,
      "cancellationRequested": false
    }
  ],
  "totalCount": 250,
  "skip": 0,
  "take": 25
}
```

The `trainState` field uses the `TrainState` enum values: `Pending`, `InProgress`, `Completed`, `Failed`.

#### curl

```bash
curl "http://localhost:5000/api/executions?skip=0&take=10"
```

### GET /executions/{id}

Returns a single execution record by database ID.

#### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `id` | path | `long` | Yes | The execution's database ID |

#### Response

`200 OK` — `ExecutionSummary`

`404 Not Found` — if no execution exists with the given ID.

#### curl

```bash
curl http://localhost:5000/api/executions/42
```

## Remarks

- All query endpoints are read-only (`GET`) and use `AsNoTracking()` for optimal performance.
- Default ordering is newest-first: manifests by ID descending, manifest groups by ID descending, executions by start time descending.
- The `totalCount` in `PagedResult` reflects the full count before pagination. Use it to calculate total pages for UI pagination.
- Execution records map to the `metadata` table in the database. Each record represents one train run — scheduled or ad-hoc.
- The `manifestId` field on `ExecutionSummary` is `null` for ad-hoc (non-scheduled) train executions.
