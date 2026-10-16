// The page chrome's data (src/lib/doc-meta.ts): type badge labels, Source
// links, package links, "More on <area>", and the H1 split.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  MORE_ON_LIMIT,
  TYPE_LABELS,
  githubPathUrl,
  moreOnArea,
  packageUrl,
  sectionOf,
  sourceLinks,
  splitLeadingH1,
} from "../src/lib/doc-meta.ts";
import { diataxisDocs, page } from "./fixtures/docs.ts";

test("only the four content types carry a badge", () => {
  assert.equal(TYPE_LABELS["how-to"], "How-to guide");
  assert.equal(TYPE_LABELS.tutorial, "Tutorial");
  assert.equal(TYPE_LABELS.reference, "Reference");
  assert.equal(TYPE_LABELS.explanation, "Explanation");
  assert.equal(TYPE_LABELS.landing, undefined);
});

test("a page's section: explicit, then samples/contributing area, then type, else standalone", () => {
  assert.equal(sectionOf(page("a", { type: "how-to", section: "Reference" })), "Reference");
  assert.equal(sectionOf(page("a", { type: "how-to", area: "samples" })), "Samples");
  assert.equal(sectionOf(page("a", { type: "reference", area: "contributing" })), "Contributing");
  assert.equal(sectionOf(page("a", { type: "explanation", area: "trains" })), "Explanation");
  assert.equal(sectionOf(page("a", { type: "landing" })), undefined);
  assert.equal(sectionOf(page("a", { type: "tutorial", section: "none" })), undefined);
  assert.equal(sectionOf(page("a")), undefined);
});

test("source links are blob URLs for files and tree URLs for directories, at the ref", () => {
  const doc = page("a", {
    source: [
      "Trax.Scheduler/src/Trax.Scheduler/Services/ITraxScheduler.cs",
      "Trax.Scheduler/src/Trax.Scheduler",
      "Trax.Core/src/Trax.Core/Junction/",
    ],
  });
  const links = sourceLinks(doc, "v1.62.0", { "Trax.Scheduler/src/Trax.Scheduler": "dir" });
  assert.deepEqual(
    links.map((l) => [l.label, l.url]),
    [
      [
        "ITraxScheduler.cs",
        "https://github.com/TraxSharp/Trax/blob/v1.62.0/Trax.Scheduler/src/Trax.Scheduler/Services/ITraxScheduler.cs",
      ],
      ["Trax.Scheduler/", "https://github.com/TraxSharp/Trax/tree/v1.62.0/Trax.Scheduler/src/Trax.Scheduler"],
      ["Junction/", "https://github.com/TraxSharp/Trax/tree/v1.62.0/Trax.Core/src/Trax.Core/Junction"],
    ]
  );
  assert.deepEqual(sourceLinks(page("b"), "main"), []);
  assert.equal(githubPathUrl("/x/y.cs", "main", "file"), "https://github.com/TraxSharp/Trax/blob/main/x/y.cs");
});

test("a package links to nuget.org, a scoped npm name to npm", () => {
  assert.equal(packageUrl("Trax.Scheduler"), "https://www.nuget.org/packages/Trax.Scheduler");
  assert.equal(packageUrl("@traxsharp/state-machine"), "https://www.npmjs.com/package/@traxsharp/state-machine");
});

test("More on <area> lists the area's other pages by type and skips the page itself", () => {
  const docs = diataxisDocs();
  const doc = docs.find((d) => d.slug === "how-to/resolve-dead-letter")!;
  const more = moreOnArea(doc, docs)!;
  assert.equal(more.areaLabel, "Scheduling");
  assert.deepEqual(
    more.groups.map((g) => [g.label, g.pages.map((p) => p.title)]),
    [
      ["Tutorials", ["Your first schedule"]],
      ["How-to guides", ["Retry a job"]],
      ["Explanation", ["How scheduling works", "Manifests"]],
      ["Reference", ["Schedule"]],
    ]
  );
});

test("More on <area> is absent on landing pages and pages without an area", () => {
  const docs = diataxisDocs();
  assert.equal(moreOnArea(docs.find((d) => d.slug === "")!, docs), undefined);
  assert.equal(moreOnArea(docs.find((d) => d.slug === "how-to/untyped-area")!, docs), undefined);
  // An area with no other typed page has nothing to list.
  assert.equal(moreOnArea(docs.find((d) => d.slug === "how-to/secure-graphql")!, docs), undefined);
});

test("More on <area> caps each type and puts SDK reference pages after the area's own", () => {
  const docs = [
    page("self", { type: "how-to", area: "data" }),
    page("sdk-reference/a", { title: "A sdk", type: "reference", area: "data", navOrder: 1 }),
    ...Array.from({ length: 8 }, (_, i) =>
      page(`ref-${i}`, { title: `Ref ${i}`, type: "reference", area: "data", navOrder: 10 + i })
    ),
  ];
  const more = moreOnArea(docs[0], docs)!;
  assert.equal(more.groups.length, 1);
  assert.equal(more.groups[0].pages.length, MORE_ON_LIMIT);
  assert.ok(!more.groups[0].pages.some((p) => p.slug.startsWith("sdk-reference/")));
});

test("the leading H1 splits off, and a page without one is left whole", () => {
  assert.deepEqual(splitLeadingH1("\n# Schedule `Run`\n\nBody.\n"), {
    heading: "# Schedule `Run`",
    rest: "\nBody.\n",
  });
  assert.equal(splitLeadingH1("Intro.\n\n# Later\n"), null);
  assert.equal(splitLeadingH1("## Not an H1\n"), null);
});
