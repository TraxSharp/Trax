// Checks that the deploy section's moving pictures stop while nobody can see them (src/lib/on-screen.ts) and draw one
// layout, not both. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { watchMedia, watchOnScreen, type ScreenEnv } from "../src/lib/on-screen.ts";

/** An IntersectionObserver and a document the test drives by hand. */
function fakeScreen() {
  let observed: ((entries: { isIntersecting: boolean }[]) => void) | null = null;
  let disconnected = false;
  const listeners = new Set<() => void>();
  const env = {
    visibilityState: "visible" as DocumentVisibilityState,
    IntersectionObserver: class {
      constructor(callback: (entries: { isIntersecting: boolean }[]) => void) {
        observed = callback;
      }
      observe() {}
      disconnect() {
        disconnected = true;
      }
    },
    document: {
      get visibilityState() {
        return env.visibilityState;
      },
      addEventListener: (_: string, f: () => void) => listeners.add(f),
      removeEventListener: (_: string, f: () => void) => listeners.delete(f),
    },
  };
  return {
    env: env as unknown as ScreenEnv,
    scroll: (on: boolean) => observed?.([{ isIntersecting: on }]),
    tab: (state: DocumentVisibilityState) => {
      env.visibilityState = state;
      for (const f of listeners) f();
    },
    get disconnected() {
      return disconnected;
    },
    get listening() {
      return listeners.size;
    },
  };
}

test("a picture runs only while it is on screen in a visible tab, every time it comes back", () => {
  const screen = fakeScreen();
  const seen: boolean[] = [];
  const stop = watchOnScreen({} as Element, (on) => seen.push(on), screen.env);
  screen.scroll(true);
  screen.scroll(false);
  screen.scroll(true);
  screen.tab("hidden");
  screen.tab("visible");
  assert.deepEqual(seen, [true, false, true, false, true]);
  assert.ok(!screen.disconnected, "it stopped watching after the first sight, so it can never pause again");
  stop();
  assert.ok(screen.disconnected);
  assert.equal(screen.listening, 0);
});

test("a hidden tab starts nothing, even with the picture in view", () => {
  const screen = fakeScreen();
  const seen: boolean[] = [];
  screen.tab("hidden");
  watchOnScreen({} as Element, (on) => seen.push(on), screen.env);
  screen.scroll(true);
  assert.deepEqual(seen, []);
  screen.tab("visible");
  assert.deepEqual(seen, [true]);
});

test("the layout follows the media query as it changes", () => {
  const listeners = new Set<() => void>();
  const list = { matches: false, addEventListener: (_: string, f: () => void) => listeners.add(f), removeEventListener: (_: string, f: () => void) => listeners.delete(f) };
  const seen: boolean[] = [];
  const stop = watchMedia("(min-width: 640px)", (m) => seen.push(m), () => list as unknown as MediaQueryList);
  list.matches = true;
  for (const f of listeners) f();
  assert.deepEqual(seen, [true]);
  stop();
  assert.equal(listeners.size, 0);
});

test("the deploy pictures pause off screen and draw one layout", () => {
  for (const file of ["ReleaseStory.tsx", "WhatYouWrite.tsx", "ReachMap.tsx"]) {
    const text = readFileSync(new URL(`../src/components/landing/${file}`, import.meta.url), "utf8");
    assert.match(text, /useOnScreen\(/, `${file} does not pause off screen`);
    assert.doesNotMatch(text, /new IntersectionObserver/, `${file} watches the screen itself`);
  }
  for (const [file, part] of [["ReleaseStory.tsx", "Picture"], ["ReachMap.tsx", "Map"]]) {
    const text = readFileSync(new URL(`../src/components/landing/${file}`, import.meta.url), "utf8");
    assert.match(text, /useMediaQuery\(/, `${file} does not pick its layout`);
    // Both layouts are drawn only until the page knows the screen's width (the server's render).
    assert.match(text, new RegExp(`wide === null \\? \\(`), `${file} draws both layouts every time`);
    assert.ok(text.includes(`<${part} `), file);
  }
});
