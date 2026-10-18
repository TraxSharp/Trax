---
title: "5.6 Read the decisions"
description: "Chapter 5 of the Trax book, section 6: Read the decisions."
type: tutorial
area: general
parent: "5. Does this order look like fraud"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/does-this-order-look-like-fraud/run-it
---

# 5.6 Read the decisions

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront -c "
  select metadata_id as run, question_key, answer->>'probability' as yes,
         routes->0->>'track' as track,
         shadows->0->'answer'->>'probability' as shadow_yes,
         shadows->0->>'agrees' as agrees
  from trax.decision order by id"
```

```text
 run |  question_key   | yes | track | shadow_yes | agrees
-----+-----------------+-----+-------+------------+--------
   1 | LooksFraudulent | 0   | No    | 1          | false
   2 | LooksFraudulent | 1   | Yes   | 1          | true
(2 rows)
```

Your run ids are higher, after the runs of the earlier chapters. Each row is one question one run
asked. `answer` is what the live rule said and `routes` the track the gate took on it. `shadows`
holds what the stricter rule said and whether it agreed: on `grace`'s order it would have held
74.00, and the live rule took it. That disagreement is the evidence for or against the stricter
rule, gathered without stopping a single customer. When the candidate has agreed often enough on
the orders that matter, it becomes the live `IDecider` and the `Shadow` line comes off the gate.

## SDK Reference

> [Gate](/docs/sdk-reference/train-methods/gate) | [Decide](/docs/sdk-reference/train-methods/decide) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Write a decider](/docs/core/write-a-decider) | [Try a decider in shadow](/docs/core/try-a-decider-in-shadow) | [Test a train's decisions](/docs/core/test-train-decisions)

---

Next: [5.7 Test it](/docs/book/does-this-order-look-like-fraud/test-it)
