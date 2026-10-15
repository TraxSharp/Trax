import { describe, expect, it } from "vitest";
import {
  ServerMachineController,
  type MachineServer,
  type ServerSnapshot,
} from "@traxsharp/state-machine/client";
import { checkout } from "./trax/checkout/checkout.machine.g";
import type { CheckoutSpec } from "./trax/checkout/checkout.contexts.g";

// The committed checkout twin opens the snapshots the host answers. Every checkout state requires a custom rule
// (the total is the server's price for the items), which the twin has no handler for: the server checked it, so a
// snapshot it sent is read, and the twin does not predict past the rule.

/** A server that answers `start` and `get` with `snapshot` and records the events it is sent. */
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

const cart = { items: ["coffee"], receipt: null, total: 999 };

describe("the checkout twin", () => {
  it("opens a Cart the host answers", async () => {
    const { server } = host({ rowId: "row-1", revision: 1, state: "Cart", context: cart });
    const controller = new ServerMachineController<CheckoutSpec>({ machine: checkout, server });

    await controller.start();

    expect(controller.get().error).toBeNull();
    expect(controller.get().status).toBe("ready");
    expect(controller.get().snapshot?.state).toBe("Cart");
    expect(controller.get().snapshot?.context).toEqual(cart);
    controller.dispose();
  });

  it("sends AddItem without predicting the total it cannot price", async () => {
    const { server, events } = host({ rowId: "row-1", revision: 1, state: "Cart", context: cart });
    const controller = new ServerMachineController<CheckoutSpec>({ machine: checkout, server });
    await controller.start();

    void controller.send("AddItem", { item: "tea" });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(events).toHaveLength(1);
    expect(events[0].clientResult).toBeUndefined();
    expect(controller.get().snapshot?.context).toEqual(cart);
    controller.dispose();
  });
});
