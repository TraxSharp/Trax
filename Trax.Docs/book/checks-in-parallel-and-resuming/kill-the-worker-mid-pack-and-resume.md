---
title: "14.5 Kill the worker mid-pack, and resume"
description: "Chapter 14 of the Trax book, section 5: Kill the worker mid-pack, and resume."
type: tutorial
area: general
parent: "14. Checks in parallel, and resuming"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/checks-in-parallel-and-resuming/fulfil-an-order
---

# 14.5 Kill the worker mid-pack, and resume

Place another order, and kill the worker while it packs, as in chapter 11:

```bash
pkill -9 -f Shopfront.Worker
```

A minute and a half later the hub has failed the run, as before. Start the worker again. As
`lin`, who is staff, find the run and ask where it can resume:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s $G -H "Authorization: Bearer $LIN" -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { executions(take: 1, trainName: \"Shopfront.Trains.FulfilOrder.IFulfilOrderTrain\", trainState: FAILED) { items { id trainState failureReason } } } }"}'
```

```json
{"data":{"operations":{"executions":{"items":[{"id":32,"trainState":"FAILED","failureReason":"Job was stuck InProgress beyond the configured stale in-progress timeout"}]}}}}
```

```bash
RUN=<the id>
curl -s $G -H "Authorization: Bearer $LIN" -H 'Content-Type: application/json' -d '{
  "query": "query($id: Long!) { operations { runGraph(metadataId: $id) { canResume allNodes { id kind state checkpointed } } } }",
  "variables": { "id": '$RUN' } }'
```

```json
{"data":{"operations":{"runGraph":{"canResume":true,"allNodes":[
  {"id":"Parallel#0","kind":"PARALLEL","state":"COMPLETED","checkpointed":false},
  {"id":"Parallel#0/box/ChooseBoxJunction#0","kind":"CHAIN","state":"COMPLETED","checkpointed":false},
  {"id":"Parallel#0/courier/GetCourierRatesJunction#0","kind":"CHAIN","state":"COMPLETED","checkpointed":false},
  {"id":"BuyLabelJunction#0","kind":"CHAIN","state":"COMPLETED","checkpointed":false},
  {"id":"Checkpoint<ShippingLabel>#0","kind":"CHECKPOINT","state":"COMPLETED","checkpointed":true},
  {"id":"PackOrderJunction#0","kind":"CHAIN","state":"INTERRUPTED","checkpointed":false},
  {"id":"Resolve#0","kind":"RESOLVE","state":"NOT_RECORDED","checkpointed":false}]}}}}
```

`runGraph` is the run drawn on its train's chain, each step with where it stands. The branches
and the label are done, the checkpoint holds the label, packing was interrupted, and `canResume`
says a resume after the checkpoint is allowed. Resume it:

```bash
curl -s $G -H "Authorization: Bearer $LIN" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: Long!) { operations { resumeExecution(id: $id) { success message id } } }",
  "variables": { "id": '$RUN' } }'
```

```json
{"data":{"operations":{"resumeExecution":{"success":true,"message":"Work queue entry 5 created.","id":5}}}}
```

The resume is queued like any run, and the worker picks it up:

```text
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packing order b1ce6f68-5f80-4b63-abcb-644536aea37e
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packed order b1ce6f68-5f80-4b63-abcb-644536aea37e as TRK-B1CE6F68
```

It neither chose a box, asked the rates nor bought a label: the run started after the checkpoint,
with the label it stored, and packed. The failed run stays failed; the resume is a run of its own,
and it completed. In the console, the failed run's page has the same as buttons: **Resume**, after its latest checkpoint, and **Resume from here** on a step.

A few things decide whether a run can resume:

- **It has to be the same chain.** The checkpoint stores a hash of the chain and the shape of the
  state. A deploy that changed either refuses the resume, with the reason, and staff requeue it
  instead, from the top.
- **Steps after the checkpoint run again** on every resume, as on any retry. `PackOrderJunction`
  packs again; a real warehouse that had half-packed a box would check for it.
- **The work before the checkpoint must have committed.** A checkpoint refuses to be stored while
  the run's Trax data context holds unsaved changes. A junction that writes, writes and saves
  before the checkpoint, so a resumed run never skips work that never happened.
- **A retry resumes too.** A manifest run that fails, the restock say, resumes after its latest
  checkpoint on its next retry, and so does a dead letter that is requeued. `requeueExecution`
  never does: it is the way to run something again from the top.

## SDK Reference

> [Parallel](/docs/sdk-reference/train-methods/parallel) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | [resumeExecution](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) | [runGraph](/docs/sdk-reference/graphql-api/queries/executions#rungraph)

---

Next: [14.6 Test it](/docs/book/checks-in-parallel-and-resuming/test-it)
