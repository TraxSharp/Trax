---
title: Drive a form from a machine
description: "Ask a machine whether a form's values may move to the next step, and where they land, with createFormView, while the form keeps its own data."
type: how-to
area: machines
nav_order: 22
---

# Drive a form from a machine

A wizard built on a form library already has a source of truth: the form. It does not want the machine to hold its
data, only to answer "given these values at this step, may I fire this, and where does it land?"
`createFormView`, from `@traxsharp/state-machine`, gives that answer from the machine's
[generated twin](/docs/statemachine/codegen-pipeline), with no server and nothing stored. A machine a server holds is
driven with [`useServerMachine`](/docs/statemachine/drive-a-machine-instance-from-react) instead.

## Map the steps and project the values

Give it the two things only the page knows: which UI step means which machine state, and how the form's values
become the machine's context.

```ts
import { createFormView } from '@traxsharp/state-machine';
import { checkout } from './trax/checkout/checkout.machine.g';

const wizard = createFormView(checkout, {
  steps: { cart: 'Cart', review: 'Review', receipt: 'Paid' },
  toContext: (values) => ({
    items: values.cart.items,
    total: values.cart.total,
    currency: 'USD',
    receipt: null,
  }),
});
```

`steps` is given in one direction only. The inverse, a state's step, is derived, so the two cannot drift, and a map
that sends two steps to one state is refused when the view is built. `toContext` may be called without its second
argument, so give it a default when you use one.

## Ask the machine

```ts
wizard.can('cart', values, 'Next');        // false while the guard declines, such as an empty cart
wizard.advance('cart', values, 'Next');    // { ok: true, step: 'review' }, or { ok: false, reason, detail }
wizard.serialize('cart', values);          // canonical JSON of the snapshot, for an autosave
wizard.resume(storedJson);                 // { step, snapshot } to reopen, or null
```

- **`advance`** fires the trigger on a snapshot built from the values and answers the step the machine moved to,
  including any branch the machine chose, or why it declined. Nothing is kept: the form still owns the values.
- **`can`** answers the same question without the step, for enabling a control.
- **`serialize`** writes the snapshot the server would store, stamped with the machine's id and version, so an
  autosave can keep it.
- **`resume`** reads a stored snapshot and answers the step to reopen it at. It answers null for a snapshot that
  does not read, and for one in a committed state (`.Committed()` in C#): a paid checkout does not reopen as a draft.
  `isCommitted(json)` answers that alone.

## Catch the server up

A form that saves on a debounce runs ahead of the server: by the time it saves, the user may be two steps on with
new values. Name the event that writes the values and the triggers that move between steps, and the view plans the
events that bring the server there.

```ts
const wizard = createFormView(signup, {
  steps: { name: 'Name', address: 'Address', done: 'Done' },
  toContext: (values) => ({ name: values.name }),
  fieldsEvent: { trigger: 'Update' },     // its input is toContext's, unless you give `input: (values) => ...`
  navigation: ['Next', 'Back'],
});

wizard.plan(serverJson, 'done', values);  // { result: 'plan', events: [Update, Next, Next] }
await wizard.sync(controller, 'done', values);
```

- **`plan`** searches from the server's snapshot, breadth first, the fields event first and then `navigation` in the
  order given. It answers the events (none when the server already holds that step and those values), `unreachable`,
  `exceeded`, `invalid`, or `undetermined` when only an edge on a custom rule the twin cannot evaluate might get
  there. It answers null when there is no server snapshot or it does not read.
- **`sync`** hands the same plan to a `ServerMachineController`'s `syncTo`, which sends it one event at a time from
  what the server confirmed, plans again after each answer and after a refusal, and gives way to a later sync. The
  controller's view is `unsaved` until one settles `synced`: hold a leave guard on it.

## SDK Reference

> [TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package) | [Codegen pipeline](/docs/statemachine/codegen-pipeline)
