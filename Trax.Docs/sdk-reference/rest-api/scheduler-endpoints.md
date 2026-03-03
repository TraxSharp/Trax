---
layout: default
title: Scheduler Endpoints
parent: REST API
grand_parent: SDK Reference
nav_order: 3
---

# Scheduler Endpoints

Eight endpoints for runtime control of scheduled manifests and manifest groups. All operations delegate to `ITraxScheduler` — the same interface used for programmatic scheduler control in C#.

All paths are relative to the route prefix (default: `/api`).

## POST /scheduler/trigger/{externalId}

Triggers a manifest for immediate execution. Creates a work queue entry that the scheduler dispatches on its next poll cycle.

### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `externalId` | path | `string` | Yes | The manifest's external ID |

### Response

`200 OK` — `OperationResponse`

```json
{
  "success": true,
  "message": "Manifest triggered"
}
```

### curl

```bash
curl -X POST http://localhost:5000/trax/api/scheduler/trigger/sync-daily
```

## POST /scheduler/trigger/{externalId}/delayed

Triggers a manifest after a specified delay. The work queue entry is created with a future dispatch time.

### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `externalId` | path | `string` | Yes | The manifest's external ID |

### Request Body

`TriggerDelayedRequest`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `delay` | `string` | Yes | Time span in `HH:MM:SS` format (e.g. `"00:30:00"` for 30 minutes) |

```json
{
  "delay": "00:30:00"
}
```

### Response

`200 OK` — `OperationResponse`

```json
{
  "success": true,
  "message": "Manifest triggered with 00:30:00 delay"
}
```

### curl

```bash
curl -X POST http://localhost:5000/trax/api/scheduler/trigger/sync-daily/delayed \
  -H "Content-Type: application/json" \
  -d '{"delay": "00:30:00"}'
```

## POST /scheduler/schedule-once

**Status: 501 Not Implemented**

Intended to schedule a one-off train execution. Currently returns `501` because `ScheduleOnceAsync` on `ITraxScheduler` requires generic type parameters that cannot be resolved from a string train name at the REST layer. A future version may add an untyped overload to `ITraxScheduler`.

### Request Body

`ScheduleOnceRequest`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `trainName` | `string` | Yes | Fully qualified type name of the train's service interface |
| `input` | `object` | Yes | JSON object matching the train's input type |
| `delay` | `string` | Yes | Time span before execution |

### Response

`501 Not Implemented`

### curl

```bash
# Returns 501
curl -X POST http://localhost:5000/trax/api/scheduler/schedule-once \
  -H "Content-Type: application/json" \
  -d '{
    "trainName": "MyApp.Trains.ISyncTrain",
    "input": {"source": "staging"},
    "delay": "01:00:00"
  }'
```

## POST /scheduler/disable/{externalId}

Disables a manifest. The scheduler skips disabled manifests during polling — no new executions are created until re-enabled.

### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `externalId` | path | `string` | Yes | The manifest's external ID |

### Response

`200 OK` — `OperationResponse`

```json
{
  "success": true,
  "message": "Manifest disabled"
}
```

### curl

```bash
curl -X POST http://localhost:5000/trax/api/scheduler/disable/sync-daily
```

## POST /scheduler/enable/{externalId}

Re-enables a previously disabled manifest.

### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `externalId` | path | `string` | Yes | The manifest's external ID |

### Response

`200 OK` — `OperationResponse`

```json
{
  "success": true,
  "message": "Manifest enabled"
}
```

### curl

```bash
curl -X POST http://localhost:5000/trax/api/scheduler/enable/sync-daily
```

## POST /scheduler/cancel/{externalId}

Requests cancellation for all in-progress executions of the given manifest. Sets the `CancellationRequested` flag on matching metadata records. The executing trains receive cancellation through their `CancellationToken`.

### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `externalId` | path | `string` | Yes | The manifest's external ID |

### Response

`200 OK` — `OperationResponse`

```json
{
  "success": true,
  "count": 2,
  "message": "Cancellation requested"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `count` | `int` | Number of in-progress executions that were marked for cancellation |

### curl

```bash
curl -X POST http://localhost:5000/trax/api/scheduler/cancel/sync-daily
```

## POST /scheduler/groups/{groupId}/trigger

Triggers all enabled manifests in a manifest group. Each manifest gets a work queue entry for the scheduler to dispatch.

### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `groupId` | path | `long` | Yes | The manifest group's database ID |

### Response

`200 OK` — `OperationResponse`

```json
{
  "success": true,
  "count": 3,
  "message": "3 manifest(s) triggered"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `count` | `int` | Number of manifests triggered in the group |

### curl

```bash
curl -X POST http://localhost:5000/trax/api/scheduler/groups/1/trigger
```

## POST /scheduler/groups/{groupId}/cancel

Requests cancellation for all in-progress executions across all manifests in a group.

### Parameters

| Parameter | In | Type | Required | Description |
|-----------|-----|------|----------|-------------|
| `groupId` | path | `long` | Yes | The manifest group's database ID |

### Response

`200 OK` — `OperationResponse`

```json
{
  "success": true,
  "count": 5,
  "message": "Cancellation requested for 5 execution(s)"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `count` | `int` | Number of in-progress executions marked for cancellation |

### curl

```bash
curl -X POST http://localhost:5000/trax/api/scheduler/groups/1/cancel
```

## Remarks

- All scheduler endpoints delegate to `ITraxScheduler`, which writes directly to the database. The scheduler server picks up the changes on its next poll cycle.
- Trigger and cancel operations are **asynchronous** in effect — the endpoint returns immediately after writing to the database. The actual execution or cancellation happens when the scheduler processes the change.
- The `groupId` parameter on group endpoints is the database `long` ID, not a string name. Use the [GET /manifest-groups]({{ site.baseurl }}{% link sdk-reference/rest-api/query-endpoints.md %}) endpoint to look up group IDs.
- Cancellation is cooperative. Setting `CancellationRequested` on a metadata record causes the scheduler to trigger the `CancellationToken` passed to the train and its steps. Trains that don't check their token will run to completion.
