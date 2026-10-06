// Checks that every /docs link written into the site's own pages leads to a
// page that exists. Run with `npm test`, which syncs Trax.Docs into .docs-cache
// first, so the pages are the ones the site would publish.

import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const DOCS_DIR = path.join(process.cwd(), ".docs-cache");
const SRC_DIR = path.join(process.cwd(), "src");

// A quoted literal such as "/docs/core/decisions" or "/docs/effect#anchor".
const DOCS_LINK = /["'`](\/docs(?:\/[A-Za-z0-9_./-]*)?)(?:#[^"'`]*)?["'`]/g;

function sourceFiles(dir: string): string[] {
  return fs
    .readdirSync(dir, { recursive: true, encoding: "utf-8" })
    .filter((f) => /\.(tsx?|mdx?)$/.test(f))
    .map((f) => path.join(dir, f));
}

function pageExists(link: string): boolean {
  const slug = link.replace(/^\/docs\/?/, "").replace(/\/$/, "");
  if (slug === "") return fs.existsSync(path.join(DOCS_DIR, "index.md"));
  return (
    fs.existsSync(path.join(DOCS_DIR, `${slug}.md`)) ||
    fs.existsSync(path.join(DOCS_DIR, slug, "index.md"))
  );
}

test("every /docs link in the site's source leads to a published page", () => {
  const missing: string[] = [];
  let checked = 0;
  for (const file of sourceFiles(SRC_DIR)) {
    const text = fs.readFileSync(file, "utf-8");
    for (const match of text.matchAll(DOCS_LINK)) {
      const link = match[1];
      // A prefix built up in code (`/docs/${slug}`) is not a page link.
      if (link.endsWith("/") && link !== "/docs/") continue;
      checked++;
      if (!pageExists(link)) missing.push(`${path.relative(SRC_DIR, file)}: ${link}`);
    }
  }
  assert.ok(checked > 0, "found no /docs links to check");
  assert.deepEqual(missing, []);
});
