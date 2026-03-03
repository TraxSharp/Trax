---
layout: default
title: Train Endpoints
parent: REST API
grand_parent: SDK Reference
nav_order: 2
---

# Train Endpoints

Three endpoints for discovering registered trains, queuing work for scheduler dispatch, and running trains directly on the API machine.

All paths are relative to the route prefix (default: `/api`).

## GET /trains

Returns all trains registered via `AddServiceTrainBus`, including their input type schema. Use this to discover available trains and build dynamic input forms.

### Response

`200 OK` — `TrainInfo[]`

```json
[
  {
    "serviceTypeName": "Trax.Samples.Api.Rest.Trains.Greet.IGreetTrain",
    "implementationTypeName": "Trax.Samples.Api.Rest.Trains.Greet.GreetTrain",
    "inputTypeName": "Trax.Samples.Api.Rest.Trains.Greet.GreetInput",
    "outputTypeName": "Unit",
    "lifetime": "Scoped",
    "inputSchema": [
      {
        "name": "Name",
        "typeName": "String",
        "isNullable": true
      }
    ]
  }
]
```

Each `TrainInfo` includes an `inputSchema` array describing the public properties of the train's input type. This is generated via reflection — property names, type names, and nullability are reported.

### curl

```bash
curl http://localhost:5000/api/trains
```

## POST /trains/queue

Creates a `WorkQueue` entry in the database. The scheduler picks it up on its next poll cycle and dispatches the train on the scheduler machine. Use this for heavy or recurring work that should run on dedicated infrastructure.

### Request Body

`QueueTrainRequest`

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `trainName` | `string` | Yes | — | Fully qualified type name of the train's service interface (e.g. `"MyApp.Trains.IOrderTrain"`) |
| `input` | `object` | Yes | — | JSON object matching the train's input type. Deserialized on the scheduler machine using the type discovered via `ITrainDiscoveryService`. |
| `priority` | `int?` | No | `0` | Dispatch priority. Higher values are dispatched first. |

```json
{
  "trainName": "Trax.Samples.Api.Rest.Trains.Greet.IGreetTrain",
  "input": { "name": "Alice" },
  "priority": 10
}
```

### Response

`200 OK` — `QueueTrainResponse`

```json
{
  "workQueueId": 42,
  "externalId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `workQueueId` | `long` | The database ID of the created work queue entry |
| `externalId` | `string` | A unique identifier for tracking this queued work item |

### curl

```bash
curl -X POST http://localhost:5000/api/trains/queue \
  -H "Content-Type: application/json" \
  -d '{
    "trainName": "Trax.Samples.Api.Rest.Trains.Greet.IGreetTrain",
    "input": { "name": "Alice" },
    "priority": 10
  }'
```

## POST /trains/run

Runs the train directly on the API machine via `ITrainBus.RunAsync`. The request blocks until the train completes. Use this for lightweight, on-demand trains where you need the result immediately.

The train's assemblies must be registered on the API machine — this endpoint calls `ITrainExecutionService.RunAsync`, which resolves the train from the DI container and executes it in-process.

### Request Body

`RunTrainRequest`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `trainName` | `string` | Yes | Fully qualified type name of the train's service interface |
| `input` | `object` | Yes | JSON object matching the train's input type |

```json
{
  "trainName": "Trax.Samples.Api.Rest.Trains.Greet.IGreetTrain",
  "input": { "name": "Bob" }
}
```

### Response

`200 OK` — `RunTrainResponse`

```json
{
  "metadataId": 17
}
```

| Field | Type | Description |
|-------|------|-------------|
| `metadataId` | `long` | The database ID of the metadata record created for this execution |

### curl

```bash
curl -X POST http://localhost:5000/api/trains/run \
  -H "Content-Type: application/json" \
  -d '{
    "trainName": "Trax.Samples.Api.Rest.Trains.Greet.IGreetTrain",
    "input": { "name": "Bob" }
  }'
```

## Remarks

- The `trainName` must match the fully qualified name of the train's **service interface** (e.g. `IGreetTrain`), not the implementation class. This is the same name returned by `GET /trains` in the `serviceTypeName` field.
- The `input` JSON is passed as a raw `JsonElement` and deserialized server-side using the input type discovered from `ITrainDiscoveryService`. If the train name doesn't match any registered train, you'll get an error response.
- **Queue vs. Run**: Queue writes to the database and returns immediately — the scheduler handles execution. Run blocks and executes in the API process. Choose based on whether you need the result inline or want to offload work.
