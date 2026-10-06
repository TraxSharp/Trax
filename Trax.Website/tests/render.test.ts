// Renders markdown through the docs pages' pipeline (next-mdx-remote with
// docsMdxOptions) and checks what reaches the HTML. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { compileMDX } from "next-mdx-remote/rsc";
import { renderToStaticMarkup } from "react-dom/server";
import { docsMdxOptions } from "../src/lib/mdx-options.ts";

async function render(markdown: string): Promise<string> {
  const { content } = await compileMDX({ source: markdown, options: docsMdxOptions });
  return renderToStaticMarkup(content);
}

test("blockquotes render as blockquotes", async () => {
  const html = await render("> **Note:** read this first.\n");
  assert.match(html, /<blockquote>\s*<p><strong>Note:<\/strong> read this first.<\/p>\s*<\/blockquote>/);
});

test("an inline anchor keeps its id unprefixed", async () => {
  const html = await render('## No warranty <a id="no-warranty"></a>\n\nSee [above](#no-warranty).\n');
  assert.match(html, /<a id="no-warranty"><\/a>/);
  assert.match(html, /href="#no-warranty"/);
});

test("generics and comparisons in prose read as written", async () => {
  const html = await render("Returns a List<string> when a > b and Task<Either<Exception, T>>.\n");
  assert.ok(html.includes("List&lt;string&gt;"), html);
  assert.ok(html.includes("a &gt; b"), html);
  assert.ok(html.includes("Task&lt;Either&lt;Exception, T&gt;&gt;"), html);
});

test("headings get slugs and fences are highlighted", async () => {
  const html = await render("## Getting started\n\n```csharp\nvar x = new List<int>();\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
  assert.match(html, /<h2 id="getting-started">/);
  assert.match(html, /data-language="csharp"/);
  assert.match(html, /<span style="[^"]+">/);
  assert.match(html, /List<\/span><span[^>]*>&lt;/);
  assert.match(html, /<table>/);
});

test("JSX and expressions are not evaluated", async () => {
  const html = await render("{1 + 1} and <Foo bar={x} /> and export const y = 1\n");
  assert.ok(!html.includes(">2<"), html);
  assert.ok(html.includes("{1 + 1}"), html);
  assert.ok(html.includes("&lt;Foo"), html);
});

const UNSAFE = [
  "<script>alert(1)</script>",
  '<script src="https://example.com/x.js"></script>',
  '<iframe src="https://example.com"></iframe>',
  '<meta http-equiv="refresh" content="0;url=https://example.com">',
  '<img src="x" onerror="alert(1)">',
  '<a href="javascript:alert(1)">click</a>',
  '[click](javascript:alert(1))',
  '<a id="ok" onclick="alert(6)" style="color:red">anchor</a>',
  '<object data="x.swf"></object>',
  '<style>body{display:none}</style>',
  "```x``` text",
  "<script>alert(2)</script>",
  "",
  "<div onclick=\"alert(3)\">\n\n*block*\n\n</div>",
  "",
  'Inline <img src=x onerror="alert(4)"> and <svg onload="alert(5)"></svg> here.',
].join("\n\n");

test("no live script, frame, meta, handler or javascript: URL survives", async () => {
  const html = await render(UNSAFE);
  for (const pattern of [
    /<script/i,
    /<iframe/i,
    /<meta/i,
    /<object/i,
    /<style/i,
    /<svg/i,
    /<div/i,
    /<img/i,
    /<[^>]*\son[a-z]+=/i,
    /href="javascript:/i,
  ]) {
    assert.doesNotMatch(html, pattern, `${pattern} in:\n${html}`);
  }
  // The text the author wrote is still visible, escaped.
  assert.ok(html.includes("&lt;script&gt;alert(2)&lt;/script&gt;"), html);
  assert.match(html, /<code>x<\/code> text/);
  // An allowed tag keeps its id and loses everything else.
  assert.match(html, /<a id="ok">anchor<\/a>/);
});
