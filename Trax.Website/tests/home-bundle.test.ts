// Checks that the home page's first load does not carry the deploy recordings: they are only read once a reader opens
// the tab or the fold that shows them. Follows every static import from the home page; an import() inside
// next/dynamic is not one. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const site = resolve(dirname(fileURLToPath(import.meta.url)), "..");

function resolveImport(from: string, spec: string): string | null {
  const base = spec.startsWith("@/") ? resolve(site, "src", spec.slice(2)) : spec.startsWith(".") ? resolve(dirname(from), spec) : null;
  if (!base) return null;
  for (const candidate of [base, `${base}.ts`, `${base}.tsx`, `${base}/index.ts`, `${base}/index.tsx`]) if (existsSync(candidate) && !candidate.endsWith("/")) return candidate;
  return null;
}

/** Every file the page reaches through static imports, with the chain that reached it. */
function reached(entry: string): Map<string, string[]> {
  const seen = new Map<string, string[]>([[entry, [entry]]]);
  const queue = [entry];
  while (queue.length > 0) {
    const file = queue.shift() as string;
    if (!/\.tsx?$/.test(file)) continue;
    const text = readFileSync(file, "utf8");
    for (const m of text.matchAll(/^\s*import\s+(?:type\s)?[^;]*?from\s+["']([^"']+)["']|^\s*import\s+["']([^"']+)["']/gm)) {
      if (/^\s*import\s+type\s/.test(m[0])) continue;
      const target = resolveImport(file, m[1] ?? m[2]);
      if (!target || seen.has(target)) continue;
      seen.set(target, [...(seen.get(file) ?? []), target]);
      queue.push(target);
    }
  }
  return seen;
}

test("the home page's first load does not import the deploy recordings", () => {
  const files = reached(resolve(site, "src/app/page.tsx"));
  const recordings = resolve(site, "src/data/deploy-recordings.json");
  assert.ok(files.has(resolve(site, "src/components/landing/ShipIt.tsx")), "the walk did not reach the deploy section");
  const chain = files.get(recordings);
  assert.equal(chain, undefined, `reached by ${chain?.map((f) => f.slice(site.length + 1)).join(" -> ")}`);
});
