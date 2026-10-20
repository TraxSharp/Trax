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
