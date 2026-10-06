import fs from "fs";
import path from "path";
import remarkGfm from "remark-gfm";
import rehypeRaw from "rehype-raw";
import rehypeSanitize, { defaultSchema } from "rehype-sanitize";
import type { Options as Schema } from "rehype-sanitize";
import rehypeSlug from "rehype-slug";
import rehypePrettyCode from "rehype-pretty-code";
import type { MDXRemoteProps } from "next-mdx-remote/rsc";
import type { Html, Paragraph, Parent, Root, Text } from "mdast";
import { visit, SKIP } from "unist-util-visit";

// This module is imported by the render tests under plain Node, so it imports
// packages only: no "@/" aliases and no relative imports.

const GITHUB_REPO = "https://github.com/TraxSharp/Trax";

/** Folder name to the ADR file paths in it, written by scripts/adr-index.mjs. */
export type AdrIndex = Record<string, string[]>;

function loadAdrIndex(): AdrIndex {
  try {
    return JSON.parse(
      fs.readFileSync(
        path.join(process.cwd(), ".docs-cache", "adr-index.json"),
        "utf-8"
      )
    );
  } catch {
    return {};
  }
}

// `Trax.Docs/adr/0026`, `Trax.Mediator/docs/adr/0004-some-title.md`, ...
const ADR_CITATION =
  /\b(Trax(?:\.[A-Za-z]+)+)\/((?:docs\/)?adr)\/(\d{4})(?:-[A-Za-z0-9-]+)?(?:\.md)?/g;

/**
 * The GitHub URL of a cited ADR: the exact file when the index knows it, else
 * the folder's ADR index, which lists every record by number. A citation names
 * the folder (`Trax.Mediator`), which is a directory of the one repository.
 */
export function adrUrl(
  repo: string,
  dir: string,
  number: string,
  index: AdrIndex
): string {
  const file = index[repo]?.find((p) => p.startsWith(`${dir}/${number}-`));
  return `${GITHUB_REPO}/blob/main/${repo}/${file ?? `${dir}/README.md`}`;
}

interface MdNode {
  type: string;
  value?: string;
  url?: string;
  children?: MdNode[];
}

function citationLink(
  match: RegExpExecArray,
  child: MdNode,
  index: AdrIndex
): MdNode {
  return {
    type: "link",
    url: adrUrl(match[1], match[2], match[3], index),
    children: [child],
  };
}

/** Splits a text node around its ADR citations, linking each one. */
function linkText(node: MdNode, index: AdrIndex): MdNode[] {
  const value = node.value ?? "";
  const out: MdNode[] = [];
  let last = 0;
  for (const match of value.matchAll(ADR_CITATION)) {
    const start = match.index ?? 0;
    if (start > last) out.push({ type: "text", value: value.slice(last, start) });
    out.push(
      citationLink(match as RegExpExecArray, { type: "text", value: match[0] }, index)
    );
    last = start + match[0].length;
  }
  if (out.length === 0) return [node];
  if (last < value.length) out.push({ type: "text", value: value.slice(last) });
  return out;
}

const NO_LINKS_INSIDE = new Set(["link", "linkReference", "definition", "code"]);

function linkCitations(node: MdNode, index: AdrIndex): void {
  if (!node.children || NO_LINKS_INSIDE.has(node.type)) return;
  node.children = node.children.flatMap((child) => {
    if (child.type === "text") return linkText(child, index);
    if (child.type === "inlineCode") {
      const match = new RegExp(`^${ADR_CITATION.source}$`).exec(child.value ?? "");
      return match ? [citationLink(match, child, index)] : [child];
    }
    linkCitations(child, index);
    return [child];
  });
}

/**
 * Turns ADR citations in prose (`Trax.Docs/adr/0026`) into GitHub links. The
 * ADRs are not published on the site, so the citation would otherwise lead
 * nowhere.
 */
export function remarkAdrLinks(options: { index?: AdrIndex } = {}) {
  const index = options.index ?? loadAdrIndex();
  return (tree: MdNode) => linkCitations(tree, index);
}

/**
 * Raw HTML tags a docs page may use. Today the docs use only `<a id="...">`
 * anchors; the rest are inline or disclosure tags that carry no behaviour.
 * Any other tag in the markdown renders as the literal text the author wrote,
 * so a generic like List<string> in prose reads as written.
 */
const RAW_HTML_TAGS = new Set([
  "a",
  "br",
  "details",
  "summary",
  "kbd",
  "sub",
  "sup",
]);

const TAG = /<\/?([A-Za-z][A-Za-z0-9-]*)(?:\s[^<>]*)?\/?>/g;
const COMMENT = /^\s*<!--[\s\S]*?-->\s*$/;

/** True when every `<` in the snippet opens a well-formed tag from RAW_HTML_TAGS. */
function isAllowedHtml(value: string): boolean {
  let tags = 0;
  for (const match of value.matchAll(TAG)) {
    if (!RAW_HTML_TAGS.has(match[1].toLowerCase())) return false;
    tags++;
  }
  return tags > 0 && tags === (value.match(/</g) ?? []).length;
}

const FLOW_PARENTS = new Set(["root", "blockquote", "listItem", "footnoteDefinition"]);

/**
 * Keeps the raw HTML nodes the docs are allowed to use and turns every other
 * one back into the text it came from. HTML comments are dropped. Working on
 * the parsed tree means code fences and code spans are never touched.
 */
export function remarkLiteralHtml() {
  return (tree: Root) => {
    visit(tree, "html", (node: Html, index, parent: Parent | undefined) => {
      if (!parent || index === undefined) return;
      if (COMMENT.test(node.value)) {
        parent.children.splice(index, 1);
        return [SKIP, index];
      }
      if (isAllowedHtml(node.value)) return;
      const text: Text = { type: "text", value: node.value };
      const replacement: Text | Paragraph = FLOW_PARENTS.has(parent.type)
        ? { type: "paragraph", children: [text] }
        : text;
      parent.children[index] = replacement as Parent["children"][number];
      return SKIP;
    });
  };
}

/**
 * What survives sanitizing: the elements markdown and GFM produce, plus
 * RAW_HTML_TAGS. Attributes and URL protocols follow the GitHub-style default
 * schema, so `href` accepts only http(s), mailto and relative URLs.
 *
 * `id`s are not prefixed: pages link to `#no-warranty` style anchors written
 * in the docs, and to the heading slugs rehype-slug adds after this step.
 */
export const docsSanitizeSchema: Schema = {
  ...defaultSchema,
  tagNames: [
    ...RAW_HTML_TAGS,
    "p",
    "h1",
    "h2",
    "h3",
    "h4",
    "h5",
    "h6",
    "blockquote",
    "ul",
    "ol",
    "li",
    "input",
    "pre",
    "code",
    "em",
    "strong",
    "del",
    "hr",
    "img",
    "table",
    "thead",
    "tbody",
    "tr",
    "th",
    "td",
    "section",
  ],
  clobber: [],
  clobberPrefix: "",
  strip: ["script", "style"],
};

/**
 * Options shared by every docs page. The docs are compiled as CommonMark
 * (`format: "md"`), not MDX, so a page cannot contain JSX or expressions.
 * Raw HTML is parsed by rehype-raw and then reduced to docsSanitizeSchema;
 * highlighting and heading slugs run after sanitizing, so their classes,
 * inline styles and ids are kept. A fence with no language is highlighted as
 * `text`, so every block carries a `data-language` and the `code` component
 * can tell it apart from inline code. ADR citations in prose become links to
 * the cited file on GitHub (remarkAdrLinks).
 */
export const docsMdxOptions: MDXRemoteProps["options"] = {
  mdxOptions: {
    format: "md",
    remarkPlugins: [remarkGfm, remarkLiteralHtml, remarkAdrLinks],
    rehypePlugins: [
      rehypeRaw,
      [rehypeSanitize, docsSanitizeSchema],
      rehypeSlug,
      [
        rehypePrettyCode,
        {
          theme: "github-dark-dimmed",
          keepBackground: false,
          defaultLang: { block: "text" },
        },
      ],
    ],
  },
};
