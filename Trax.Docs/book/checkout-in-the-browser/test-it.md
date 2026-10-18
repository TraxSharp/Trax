---
title: "16.4 Test it"
description: "Chapter 16 of the Trax book, section 4: Test it."
type: tutorial
area: general
parent: "16. Checkout in the browser"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/checkout-in-the-browser/run-it
---

# 16.4 Test it

The twin can be tested without a hub. A test gives the hook's controller a fake server that
answers with a snapshot and never answers an event, so what it shows is the twin's prediction. Add
Vitest:

`web/package.json`:

```json file=web/package.json
{
  "name": "shopfront-web",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "test": "vitest run"
  },
  "dependencies": {
    "@traxsharp/state-machine": "1.67.0",
    "graphql": "^16.10.0",
    "graphql-ws": "^6.0.0",
    "react": "^19.3.0",
    "react-dom": "^19.3.0"
  },
  "devDependencies": {
    "@types/react": "^19.3.0",
    "@types/react-dom": "^19.3.0",
    "@vitejs/plugin-react": "^6.1.2",
    "typescript": "~5.9.3",
    "vite": "^8.3.3",
    "vitest": "^4.1.12"
  }
}
```

`web/src/checkout.test.ts`:

```ts file=web/src/checkout.test.ts
import { describe, expect, it } from "vitest";
import { ServerMachineController, type MachineServer, type ServerSnapshot } from "@traxsharp/state-machine/client";
import { checkout } from "./trax/checkout/checkout.machine.g";

// A server that answers with `snapshot`, records the events it is sent, and never answers one, so
// what the page shows is the twin's prediction.
function host(snapshot: ServerSnapshot) {
  const events: Parameters<MachineServer["event"]>[0][] = [];
  const server: MachineServer = {
    start: async () => ({ kind: "started", id: "c-1", snapshot }),
    get: async () => ({ kind: "ok", snapshot }),
    event: async (request) => {
      events.push(request);
      return new Promise(() => {});
    },
    subscribe: () => () => {},
  };
  return { server, events };
}

async function open(context: Record<string, unknown>) {
  const { server, events } = host({ rowId: "row-1", revision: 1, state: "Cart", context });
  const controller = new ServerMachineController({ machine: checkout, server });
  await controller.start();
  return { controller, events };
}

describe("the checkout twin", () => {
  it("will not take an address for an empty cart", async () => {
    const { controller } = await open({ lines: [] });

    expect(controller.can("SetAddress", { address: "1 Main St" })).toBe(false);
    controller.dispose();
  });

  it("takes an address once the cart has something in it", async () => {
    const { controller } = await open({ lines: [{ sku: "MUG", quantity: 2 }] });

    expect(controller.can("SetAddress", { address: "1 Main St" })).toBe(true);
    expect(controller.can("SetAddress", { address: "" })).toBe(false);
    controller.dispose();
  });

  it("shows new lines at once, and sends the server what it predicted", async () => {
    const { controller, events } = await open({ lines: [] });

    void controller.send("SetLines", { lines: [{ sku: "MUG", quantity: 1 }] });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(controller.get().snapshot?.context).toEqual({ lines: [{ sku: "MUG", quantity: 1 }] });
    expect(events).toHaveLength(1);
    expect(events[0].clientResult).toBeDefined();
    controller.dispose();
  });
});
```

```bash
npm install
npm test
```

```text
 ✓ src/checkout.test.ts (3 tests)

 Test Files  1 passed (1)
      Tests  3 passed (3)
```

The hub's side is tested as it was: the C# tests replay the corpus against the machine, and the
twin is generated from the same definition.

## What we have

The customer checks out in the browser. Each click shows its result at once, the hub decides and
stores every move, and the moves the hub makes on its own arrive as they happen.

Everything the storefront sends is a document the hub registered. The next chapter makes that the
only thing it accepts.

## SDK Reference

> [Call a machine from TypeScript](/docs/statemachine/call-a-machine-from-typescript) | [The TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package)

---

Next: [17. Require persisted operations](/docs/book/require-persisted-operations)
