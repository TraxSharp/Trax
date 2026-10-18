---
title: "10.7 Bring it back"
description: "Chapter 10 of the Trax book, section 7: Bring it back."
type: tutorial
area: general
parent: "10. Failures, retries and dead letters"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/failures-retries-and-dead-letters/watch-it-fail
---

# 10.7 Bring it back

```bash
curl -X POST http://localhost:5080/dev/supplier/up
```

The dead letter is still there: fixing the cause does not clear it, because nothing knows the cause
was fixed. In the console, open it under **Dead letters** and choose **Requeue**. The same, as a
member of staff:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { deadLetters { deadLetters(take: 5) { items { id manifestName status reason } } } } }"}'
```

```json
{"data":{"operations":{"deadLetters":{"deadLetters":{"items":[{"id":1,"manifestName":"Shopfront.Trains.Restock.IRestockTrain","status":"AWAITING_INTERVENTION","reason":"Max retries exceeded: (3) failures > (2) max retries"}]}}}}}
```

```bash
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"mutation { operations { deadLetters { requeueDeadLetter(id: 1) { success message } } } }"}'
```

```json
{"data":{"operations":{"deadLetters":{"requeueDeadLetter":{"success":true,"message":"Dead letter requeued"}}}}}
```

The requeue runs at once, with no backoff, and succeeds. The dead letter is resolved, the manifest
is back on its schedule, and its failures no longer count toward the next dead letter.

**Acknowledge** is the other way to resolve one: it records a note and puts the manifest back on
its schedule without running it, for a failure that needs no run to make up for it.

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)

---

Next: [10.8 Test it](/docs/book/failures-retries-and-dead-letters/test-it)
