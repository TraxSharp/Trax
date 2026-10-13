// The redirects generated from redirect_from (scripts/docs-index.mjs), which
// next.config.ts serves as permanent redirects.
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { collectRedirects, collectSourceKinds, normalizeRedirect, readPages } from "../scripts/docs-index.mjs";

test("each redirect_from entry becomes a permanent redirect to the page", () => {
  const redirects = collectRedirects([
    { file: "index.md", data: { redirect_from: "/docs/home" } },
    {
      file: "scheduling/how-to/resolve-dead-letter.md",
      data: { redirect_from: ["/docs/scheduler/dead-letters-and-cleanup", "docs/scheduler/dead-letters/"] },
    },
    { file: "no-redirects.md", data: { title: "x" } },
  ]);
  assert.deepEqual(redirects, [
    { source: "/docs/home", destination: "/docs", permanent: true },
    {
      source: "/docs/scheduler/dead-letters",
      destination: "/docs/scheduling/how-to/resolve-dead-letter",
      permanent: true,
    },
    {
      source: "/docs/scheduler/dead-letters-and-cleanup",
      destination: "/docs/scheduling/how-to/resolve-dead-letter",
      permanent: true,
    },
  ]);
});

test("a redirect that would hide a live page, or that two pages claim, fails", () => {
  assert.throws(
    () =>
      collectRedirects([
        { file: "a.md", data: { redirect_from: "/docs/b" } },
        { file: "b.md", data: {} },
      ]),
    /\/docs\/b is a published page/
  );
  assert.throws(
    () =>
      collectRedirects([
        { file: "a.md", data: { redirect_from: "/docs/old" } },
        { file: "b.md", data: { redirect_from: "/docs/old" } },
      ]),
    /also claimed by a.md/
  );
});

test("redirect paths are normalized", () => {
  assert.equal(normalizeRedirect("docs/x"), "/docs/x");
  assert.equal(normalizeRedirect("/docs/x/"), "/docs/x");
  assert.equal(normalizeRedirect("/docs/x.md"), "/docs/x");
});

test("the script reads front matter from the cache and stats source paths", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "docs-index-"));
  try {
    fs.mkdirSync(path.join(dir, "cache", "guide"), { recursive: true });
    fs.mkdirSync(path.join(dir, "repo", "Trax.X", "src"), { recursive: true });
    fs.writeFileSync(path.join(dir, "repo", "Trax.X", "src", "A.cs"), "");
    fs.writeFileSync(
      path.join(dir, "cache", "guide", "page.md"),
      "---\ntitle: Page\nredirect_from:\n  - /docs/old-page\nsource:\n  - Trax.X/src/A.cs\n  - Trax.X/src\n  - Trax.X/gone.cs\n---\n# Page\n"
    );
    const pages = readPages(path.join(dir, "cache"));
    assert.deepEqual(collectRedirects(pages), [
      { source: "/docs/old-page", destination: "/docs/guide/page", permanent: true },
    ]);
    assert.deepEqual(collectSourceKinds(pages, path.join(dir, "repo")), {
      "Trax.X/src/A.cs": "file",
      "Trax.X/src": "dir",
      "Trax.X/gone.cs": "missing",
    });
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test("the synced docs produced a redirects file next.config.ts can read", () => {
  const file = path.join(process.cwd(), ".docs-cache", "redirects.json");
  const redirects = JSON.parse(fs.readFileSync(file, "utf-8"));
  assert.ok(Array.isArray(redirects));
  for (const r of redirects) {
    assert.equal(r.permanent, true);
    assert.match(r.source, /^\//);
    assert.match(r.destination, /^\/docs/);
  }
});
