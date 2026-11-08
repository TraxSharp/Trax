// Takes the screenshot each GUI sample's README shows, by driving the running sample through what it
// demonstrates. One sample at a time: start it the way its README says, then run
//
//   node shoot.mjs <recovery|chat-service|state-machine|signalr-broadcaster|bakery> [--copy-to <dir>]
//
// The image is written to samples/<Sample>/screenshot.png. --copy-to also writes it to <dir>/<scene>.png.
//
// A sample's local data shows up in the shot, so start it with fresh data for a clean one.
// CHROMIUM_PATH points Playwright at a Chromium it did not install.

import { chromium } from "playwright";
import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const samples = resolve(here, "../../samples");
const VIEWPORT = { width: 1600, height: 900 };

const scenes = {
  // A refund run that crashes while it pays and recovers: the retry reuses the recorded answers instead of asking
  // the model. The page opens on the food order, so the Workflows view is picked first.
  recovery: {
    folder: "Recovery",
    url: "http://localhost:5173",
    async play(browser) {
      const page = await open(browser);
      await page.getByRole("tab", { name: "Workflows" }).click();
      await page.getByLabel("Crash").selectOption("paying");
      await page.getByRole("button", { name: "Run", exact: true }).click();
      await page.locator(".timeline-grid .status", { hasText: "Recovered" }).waitFor({ timeout: 60_000 });
      await page.waitForTimeout(500);
      return page;
    },
  },

  // Alice invites Bob into a room and they talk; Alice's page shows the traffic under the hood.
  "chat-service": {
    folder: "ChatService",
    url: "http://localhost:5173",
    async play(browser) {
      const alice = await open(browser);
      const bob = await open(browser);
      await bob.getByRole("radio", { name: /Bob/ }).click();
      await alice.getByRole("button", { name: "+ New room" }).click();
      await alice.getByPlaceholder("Room name").fill("Release planning");
      await alice.getByRole("button", { name: "Create room" }).click();
      await alice.getByRole("button", { name: "+ Invite" }).click();
      await alice.locator(".invite-list li", { hasText: "Bob" }).getByRole("button", { name: "Add" }).click();
      await alice.locator(".invite-in").first().waitFor();
      await alice.keyboard.press("Escape");
      const room = bob.locator(".room-item", { hasText: "Release planning" });
      await room.waitFor({ timeout: 15_000 });
      await room.click();
      const say = async (page, text) => {
        await page.locator(".chat-input input").fill(text);
        await page.locator(".chat-input input").press("Enter");
        await page.waitForTimeout(600);
      };
      await say(alice, "Core and Effect are released. Scheduler next?");
      await say(bob, "Cutting it now. Api and Dashboard after that.");
      await say(alice, "Great, I'll bump the pins in Samples.");
      await alice.waitForTimeout(800);
      await bob.close();
      return alice;
    },
  },

  // The checkout pays once: the walkthrough, the side effect's binding, and the provider's single charge.
  "state-machine": {
    folder: "StateMachine",
    url: "http://localhost:5173",
    // Tall enough that the payment provider's list shows under the walkthrough.
    viewport: { width: 1600, height: 1080 },
    async play(browser) {
      const page = await open(browser);
      await page.locator(".tab", { hasText: "A machine with an effect" }).click();
      await page.waitForTimeout(1200);
      const state = async () => (await page.locator(".badge").textContent()).trim();
      const fire = async (caption) => {
        await page.locator(".trigger", { hasText: caption }).click();
        await page.waitForTimeout(700);
      };
      if ((await state()) === "Paid") await fire("start over");
      if ((await state()) === "Review") await fire("back to the cart");
      for (const item of ["Field guide", "Compass"]) {
        await page.getByPlaceholder("Add an item").fill(item);
        await page.getByRole("button", { name: "Save with it" }).click();
        await page.waitForTimeout(500);
      }
      await fire("go to review");
      await fire("pay: runs the charge");
      await fire("pay: runs the charge");
      return page;
    },
  },

  // Signed in, three pings: one completes, one fails with a reason shown, one with the reason withheld.
  "signalr-broadcaster": {
    folder: "SignalRBroadcaster",
    url: "http://localhost:5270",
    async play(browser) {
      const page = await open(browser);
      await page.getByRole("button", { name: "Sign in as the demo operator" }).click();
      await page.locator(".pill.connected").waitFor();
      for (const outcome of ["Succeed", "FailForClients", "FailUnexpectedly", "Succeed"]) {
        await page.locator(`button[data-outcome="${outcome}"]`).click();
        await page.waitForTimeout(900);
      }
      return page;
    },
  },

  // Ada orders three loaves and places them; Kit clocks in, the shop opens and her order goes in the oven. The shot
  // is Ada's page, with the scenario that ends where her order is lit in its feature. BAKERY_URL points it at a page
  // served elsewhere.
  bakery: {
    folder: "Bakery",
    url: process.env.BAKERY_URL ?? "http://localhost:5281",
    viewport: { width: 1600, height: 1000 },
    async play(browser) {
      const signIn = async (name, role) => {
        const page = await open(browser);
        await page.getByLabel("Name").fill(name);
        await page.getByLabel("Role").selectOption(role);
        await page.getByRole("button", { name: "Sign in" }).click();
        await page.getByText(`Signed in as ${name}`).waitFor();
        return page;
      };
      const ada = await signIn("Ada", "customer");
      await ada.locator("[data-order-state]", { hasText: "Draft" }).waitFor({ timeout: 15_000 });
      await ada.getByLabel("Loaves").fill("3");
      await ada.getByRole("button", { name: "Order", exact: true }).click();
      await ada.getByRole("button", { name: "Place", exact: true }).click();
      await ada.getByRole("region", { name: "Your orders" }).getByText("Waiting for the oven").waitFor({ timeout: 15_000 });
      // A shop still open from an earlier run would not open again, and nothing would bake: wait for it to close.
      await ada.locator("[data-shop-sign]", { hasText: "Closed" }).waitFor({ timeout: 180_000 });
      const kit = await signIn("Kit", "baker");
      await kit.getByRole("button", { name: "Clock in" }).click();
      await ada.locator("[data-order-state]", { hasText: "Baking" }).waitFor({ timeout: 30_000 });
      await ada.waitForTimeout(1200);
      await kit.close();
      return ada;
    },
  },
};

async function open(browser) {
  const context = await browser.newContext({ viewport: current.viewport ?? VIEWPORT, colorScheme: "dark" });
  const page = await context.newPage();
  await page.goto(current.url);
  await page.waitForTimeout(1200);
  return page;
}

const [name, flag, copyTo] = process.argv.slice(2);
const current = scenes[name];
if (!current || (flag && flag !== "--copy-to") || (flag && !copyTo)) {
  console.error(`usage: node shoot.mjs <${Object.keys(scenes).join("|")}> [--copy-to <dir>]`);
  process.exit(2);
}

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined });
try {
  const page = await current.play(browser);
  const out = join(samples, current.folder, "screenshot.png");
  await page.screenshot({ path: out });
  console.log(`wrote ${out}`);
  if (copyTo) {
    mkdirSync(resolve(copyTo), { recursive: true });
    const copy = join(resolve(copyTo), `${name}.png`);
    copyFileSync(out, copy);
    console.log(`wrote ${copy}`);
  }
} finally {
  await browser.close();
}
