// Renders docs pages through the same remark/rehype pipeline the site uses and
// checks the HTML. Run with `npm test`, which syncs Trax.Docs into .docs-cache
// first, so the pages are the ones the site would publish.

import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { evaluate } from "@mdx-js/mdx";
import * as runtime from "react/jsx-runtime";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import matter from "gray-matter";
import { docsMdxOptions, remarkAdrLinks } from "../src/lib/mdx-options.ts";

const DOCS_DIR = path.join(process.cwd(), ".docs-cache");

async function render(markdown: string): Promise<string> {
  const { default: Content } = await evaluate(markdown, {
    ...docsMdxOptions!.mdxOptions,
    ...runtime,
  } as Parameters<typeof evaluate>[1]);
  return renderToStaticMarkup(createElement(Content));
}

async function renderPage(file: string): Promise<string> {
  const raw = fs.readFileSync(path.join(DOCS_DIR, file), "utf-8");
  return render(matter(raw).content);
}

test("api-security renders its disclaimer as a blockquote and its anchor as an element", async () => {
  const html = await renderPage("api-security.md");
  // Booleans rather than assert.match, which would print the whole page.
  assert.ok(/<blockquote>\s*<p>NO WARRANTY/.test(html), "no <blockquote> for the disclaimer");
  assert.ok(html.includes('<a id="no-warranty"></a>'), 'no element with id "no-warranty"');
  assert.ok(!html.includes("<p>&gt; "), "a blockquote rendered as text");
  assert.ok(!html.includes("&lt;a id="), "raw HTML rendered as text");
});

test("angle brackets that do not open a tag stay text", async () => {
  const html = await render(
    "### ServiceTrain<TIn, TOut>\n\nCalls `Run<T>()` on IJunction<TIn, TOut>.\n"
  );
  assert.match(html, /ServiceTrain&lt;TIn, TOut&gt;<\/h3>/);
  assert.match(html, /on IJunction&lt;TIn, TOut&gt;\./);
  assert.match(html, /<code[^>]*>Run&lt;T&gt;\(\)<\/code>/);
});

test("braces are text, not expressions", async () => {
  const html = await render("Query `discover` under { namespace }.\n");
  assert.match(html, /under \{ namespace \}\./);
});

test("ADR citations link to the file on GitHub, or to the repo's ADR index", async () => {
  const index = {
    "Trax.Docs": ["adr/0026-role-comparison-is-ordinal.md"],
  };
  const { default: Content } = await evaluate(
    "See `Trax.Docs/adr/0026` and Trax.Mediator/docs/adr/0004.\n",
    {
      ...runtime,
      format: "md",
      remarkPlugins: [[remarkAdrLinks, { index }]],
    } as Parameters<typeof evaluate>[1]
  );
  const html = renderToStaticMarkup(createElement(Content));
  assert.match(
    html,
    /<a href="https:\/\/github.com\/TraxSharp\/Trax\/blob\/main\/Trax.Docs\/adr\/0026-role-comparison-is-ordinal.md"><code>Trax.Docs\/adr\/0026<\/code><\/a>/
  );
  assert.match(
    html,
    /<a href="https:\/\/github.com\/TraxSharp\/Trax\/blob\/main\/Trax.Mediator\/docs\/adr\/README.md">Trax.Mediator\/docs\/adr\/0004<\/a>/
  );
});

test("every published page compiles", async () => {
  const files = fs
    .readdirSync(DOCS_DIR, { recursive: true, encoding: "utf-8" })
    .filter((f) => f.endsWith(".md"));
  assert.ok(files.length > 100, `only ${files.length} pages in ${DOCS_DIR}`);
  for (const file of files) {
    await assert.doesNotReject(renderPage(file), file);
  }
});
