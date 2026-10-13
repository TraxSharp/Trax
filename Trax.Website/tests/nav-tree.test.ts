// The sidebar built from front matter (src/lib/nav-tree.ts): sections from
// type and area, area groups, parent nesting, ordering, and today's pages
// that carry none of the new fields.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  buildNavTree,
  containsSlug,
  getDocsSections,
  type NavEntry,
  type NavSection,
} from "../src/lib/nav-tree.ts";
import { diataxisDocs, legacyDocs, page } from "./fixtures/docs.ts";

/** A compact picture of the tree: groups as "[Label]", pages by title, children nested. */
function shape(entries: NavEntry[]): unknown[] {
  return entries.map((e) =>
    e.kind === "group"
      ? { [`[${e.label}]`]: shape(e.items) }
      : e.children.length > 0
        ? { [e.title]: shape(e.children) }
        : e.title
  );
}

function section(tree: NavSection[], label: string): NavSection {
  const found = tree.find((s) => s.label === label);
  assert.ok(found, `no section ${label}`);
  return found;
}

test("sections come in the fixed order, with Home standing alone first", () => {
  const tree = buildNavTree(diataxisDocs());
  assert.deepEqual(
    tree.map((s) => s.label),
    [null, "Tutorials", "How-to guides", "Explanation", "Reference", "SDK Reference", "Samples", "Contributing"]
  );
  assert.deepEqual(shape(tree[0].items), ["Home"]);
});

test("a section spanning several areas groups its pages by area, in area order", () => {
  const tree = buildNavTree(diataxisDocs());
  assert.deepEqual(shape(section(tree, "Tutorials").items), [
    { "[Trains]": ["Your first train"] },
    { "[Scheduling]": ["Your first schedule"] },
  ]);
  // A page without an area is filed under General; ties on nav_order sort by title.
  assert.deepEqual(shape(section(tree, "How-to guides").items), [
    { "[General]": ["Untyped area page"] },
    { "[Trains]": ["Add a junction"] },
    { "[Scheduling]": ["Resolve a dead letter", "Retry a job"] },
    { "[Security]": ["Secure a GraphQL host"] },
  ]);
});

test("a single-area section and SDK Reference show no area groups", () => {
  const tree = buildNavTree(diataxisDocs());
  assert.deepEqual(shape(section(tree, "Explanation").items), [
    { "How scheduling works": ["Manifests"] },
  ]);
  assert.deepEqual(shape(section(tree, "SDK Reference").items), [
    { "SDK Reference": [{ "Scheduler API": ["Schedule"] }] },
    "Core API",
  ]);
});

test("area samples and contributing pick their sections over the type's", () => {
  const tree = buildNavTree(diataxisDocs());
  assert.deepEqual(shape(section(tree, "Samples").items), ["Chat service"]);
  assert.deepEqual(shape(section(tree, "Contributing").items), ["About these docs", "Writing docs"]);
});

test("an explicit section beats the type", () => {
  const tree = buildNavTree([
    page("", { title: "Home", type: "landing" }),
    page("x", { title: "Explained but filed in Reference", type: "explanation", section: "Reference" }),
  ]);
  assert.deepEqual(tree.map((s) => s.label), [null, "Reference"]);
});

test("pages without the new fields build today's sidebar", () => {
  const tree = buildNavTree(legacyDocs());
  assert.deepEqual(
    tree.map((s) => s.label),
    [null, null, "Reference", "SDK Reference", "Packages", "Guides"]
  );
  assert.deepEqual(shape(tree[1].items), ["Getting Started"]);
  assert.deepEqual(shape(section(tree, "Packages").items), [
    { Core: ["Building Chains", "Memory"] },
  ]);
  assert.deepEqual(shape(section(tree, "Guides").items), [
    { "Cross-Cutting": [{ "DI Registration": ["Train Registration"] }] },
  ]);
});

test("grand_parent picks the right parent when titles repeat", () => {
  const tree = buildNavTree([
    page("a", { title: "A", section: "Reference", navOrder: 1 }),
    page("a/options", { title: "Options", parent: "A" }),
    page("b", { title: "B", section: "Reference", navOrder: 2 }),
    page("b/options", { title: "Options", parent: "B" }),
    page("b/options/x", { title: "X", parent: "Options", grandParent: "B" }),
  ]);
  assert.deepEqual(shape(section(tree, "Reference").items), [
    { A: ["Options"] },
    { B: [{ Options: ["X"] }] },
  ]);
});

test("a page whose parent does not exist still appears, at the top of its section", () => {
  const tree = buildNavTree([
    page("orphan", { title: "Orphan", type: "how-to", parent: "Gone" }),
  ]);
  assert.deepEqual(shape(section(tree, "How-to guides").items), ["Orphan"]);
});

test("containsSlug finds the current page inside a group", () => {
  const tree = buildNavTree(diataxisDocs());
  const [general, , scheduling] = section(tree, "How-to guides").items;
  assert.equal(containsSlug(scheduling, "how-to/retry-a-job"), true);
  assert.equal(containsSlug(general, "how-to/retry-a-job"), false);
  const explanation = section(tree, "Explanation").items[0];
  assert.equal(containsSlug(explanation, "explanation/scheduling/manifests"), true);
});

test("llms.txt sections follow the sidebar, standalone pages under Overview", () => {
  const sections = getDocsSections(diataxisDocs());
  assert.deepEqual(
    sections.map((s) => s.label),
    ["Overview", "Tutorials", "How-to guides", "Explanation", "Reference", "SDK Reference", "Samples", "Contributing"]
  );
  assert.deepEqual(
    sections.find((s) => s.label === "How-to guides")!.pages.map((p) => p.doc.title),
    ["Untyped area page", "Add a junction", "Resolve a dead letter", "Retry a job", "Secure a GraphQL host"]
  );
  assert.deepEqual(
    sections.find((s) => s.label === "SDK Reference")!.pages.map((p) => p.doc.title),
    ["SDK Reference", "Scheduler API", "Schedule", "Core API"]
  );
  const total = sections.reduce((n, s) => n + s.pages.length, 0);
  assert.equal(total, diataxisDocs().length, "a page is missing from llms.txt");
});
