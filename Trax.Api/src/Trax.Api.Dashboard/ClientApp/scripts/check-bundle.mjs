// Checks what a production build carries. The normal build (`app`) must hold none of the mock or
// the demo: no embedded schema, no mock store, no recordings. The demo build (`demo`) carries the
// recordings and the mock's store and overlays, but not the auto-mock schema, so a page it has no
// recording for says so instead of showing invented data.
//
//   node scripts/check-bundle.mjs dist app
//   node scripts/check-bundle.mjs dist-demo demo
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

const [dir, kind] = process.argv.slice(2);
if (!dir || !["app", "demo"].includes(kind)) {
  console.error("usage: node scripts/check-bundle.mjs <dist dir> app|demo");
  process.exit(2);
}

const files = [];
(function list(d) {
  for (const name of readdirSync(d)) {
    const path = join(d, name);
    if (statSync(path).isDirectory()) list(path);
    else files.push(path);
  }
})(dir);
const has = (marker) => files.some((f) => readFileSync(f, "utf8").includes(marker));

const SCHEMA = "The purpose of the `cost` directive"; // src/mock/schema-sdl.ts
const MOCK_STORE = "trax:mock-overlay"; // src/mock/store/mock-store.ts
const RECORDINGS = "dashboard-recordings"; // src/demo/data
const BANNER = "recorded data, changes are simulated"; // src/components/DemoBanner.tsx

const rules =
  kind === "app"
    ? [
        [SCHEMA, false, "the auto-mock schema"],
        [MOCK_STORE, false, "the mock store"],
        [RECORDINGS, false, "the demo's recordings"],
        [BANNER, false, "the demo banner"],
      ]
    : [
        [SCHEMA, false, "the auto-mock schema"],
        [RECORDINGS, true, "the recordings"],
        [BANNER, true, "the demo banner"],
      ];

let failed = false;
for (const [marker, wanted, what] of rules) {
  if (has(marker) !== wanted) {
    console.error(`${dir}: ${wanted ? "missing" : "carries"} ${what}`);
    failed = true;
  }
}
if (failed) process.exit(1);
console.log(`${dir}: the ${kind} build carries what it should`);
