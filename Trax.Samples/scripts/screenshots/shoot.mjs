// Takes the screenshot each GUI sample's README shows, by driving the running sample through what it
// demonstrates. One sample at a time: start it the way its README says, then run
//
//   node shoot.mjs <recovery|chat-service|state-machine|signalr-broadcaster> [--copy-to <dir>]
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
  // A refund run that crashes and recovers: the retry reuses the recorded answer instead of asking the model.
  recovery: {
    folder: "Recovery",
    url: "http://localhost:5173",
    async play(browser) {
      const page = await open(browser);
      await page.getByRole("button", { name: "Run", exact: true }).click();
      await page.locator(".status", { hasText: "Recovered" }).waitFor({ timeout: 60_000 });
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
