import fs from "fs";
import path from "path";
import matter from "gray-matter";
import { SITE_URL } from "./site";

export interface DocPage {
  slug: string;
  title: string;
  navOrder: number;
  parent?: string;
  grandParent?: string;
  hasChildren: boolean;
  section?: string;
  filePath: string;
  /** Path of the page in the Trax.Docs repo, e.g. `effect/host-tracking.md`. */
  sourcePath: string;
  /** The page's markdown with the front matter removed: rendered by the docs pages and served raw. */
  body: string;
  /** Front-matter `description`, else the first prose paragraph, else a generic line. */
  description: string;
}

const DOCS_DIR = path.join(process.cwd(), ".docs-cache");

function getSlugFromPath(filePath: string): string {
  const relative = path.relative(DOCS_DIR, filePath);
  // index.md at root becomes empty string (docs home)
  if (relative === "index.md") return "";
  // Remove .md extension
  const slug = relative.replace(/\.md$/, "");
  return slug;
}

function getAllMarkdownFiles(dir: string): string[] {
  const files: string[] = [];
  if (!fs.existsSync(dir)) return files;

  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...getAllMarkdownFiles(fullPath));
    } else if (entry.name.endsWith(".md")) {
      files.push(fullPath);
    }
  }
  return files;
}

function collapse(text: string): string {
  return text.replace(/\s+/g, " ").trim();
}

function truncate(text: string, max = SUMMARY_MAX): string {
  if (text.length <= max) return text;
  const cut = text.slice(0, max - 1);
  const lastSpace = cut.lastIndexOf(" ");
  return `${(lastSpace > max / 2 ? cut.slice(0, lastSpace) : cut).replace(/[\s,;:.]+$/, "")}…`;
}

const SUMMARY_MAX = 160;

// A line that starts something other than a prose paragraph.
const NON_PROSE =
  /^\s*(#|\||>|[-*+]\s|\d+[.)]\s|<|!\[|```|~~~|---+\s*$|===+\s*$|\{:|\[\^)/;

/**
 * The first prose paragraph of a page as plain text, trimmed to ~160 characters.
 * Headings, lists, tables, quotes, HTML and code are skipped, and so is a short
 * lead-in that ends in a colon ("Trax requires net10.0:"), since it only
 * introduces the block below it.
 */
export function summarize(markdown: string): string {
  const lines = markdown.split("\n");
  let inFence = false;
  let paragraph: string[] = [];
  let fallback = "";

  const flush = (): string | undefined => {
    if (paragraph.length === 0) return undefined;
    const text = collapse(
      paragraph
        .join(" ")
        .replace(/!\[[^\]]*\]\([^)]*\)/g, "")
        .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
        .replace(/\*\*([^*]+)\*\*/g, "$1")
        .replace(/`([^`]*)`/g, "$1")
        .replace(/<[^>]+>/g, "")
    );
    paragraph = [];
    if (!text) return undefined;
    if (!fallback) fallback = text;
    if (text.endsWith(":") && text.length < 80) return undefined;
    return text;
  };

  for (const line of lines) {
    if (/^\s*(```|~~~)/.test(line)) {
      const found = flush();
      if (found) return truncate(found);
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    if (line.trim() === "" || NON_PROSE.test(line)) {
      const found = flush();
      if (found) return truncate(found);
      continue;
    }
    paragraph.push(line.trim());
  }
  const found = flush();
  return truncate(found ?? fallback);
}

export function getAllDocs(): DocPage[] {
  const files = getAllMarkdownFiles(DOCS_DIR);
  return files.map((filePath) => {
    const raw = fs.readFileSync(filePath, "utf-8");
    const { data, content } = matter(raw);
    // A page with no front matter (SECURITY.md) is titled by its H1, not its
    // file name, so the sidebar does not show "SECURITY".
    const title: string =
      data.title ||
      content.match(/^#\s+(.+?)\s*#*\s*$/m)?.[1] ||
      path.basename(filePath, ".md");
    return {
      slug: getSlugFromPath(filePath),
      title,
      navOrder: data.nav_order ?? 999,
      parent: data.parent,
      grandParent: data.grand_parent,
      hasChildren: data.has_children ?? false,
      section: data.section,
      filePath,
      sourcePath: path.relative(DOCS_DIR, filePath).split(path.sep).join("/"),
      body: content,
      description:
        (typeof data.description === "string" && data.description.trim()
          ? truncate(collapse(data.description))
          : summarize(content)) || `Trax documentation: ${title}`,
    };
  });
}

let cache: DocPage[] | undefined;

/** Every published page, parsed once per process. */
function allDocsCached(): DocPage[] {
  cache ??= getAllDocs();
  return cache;
}

export function getDocBySlug(slug: string): DocPage | undefined {
  return allDocsCached().find((doc) => doc.slug === slug);
}

/**
 * The page's <title>. Titles are not unique (Host Tracking is both a guide and
 * an SDK reference page), so a title another page shares is followed by the
 * page's top-level section.
 */
export function documentTitle(doc: DocPage): string {
  const shared = allDocsCached().some(
    (other) => other !== doc && other.title === doc.title
  );
  const section = doc.grandParent ?? doc.parent ?? doc.section;
  return shared && section ? `${doc.title} - ${section}` : doc.title;
}

/** The page's source file on GitHub, for readers who want to edit or cite it. */
export function sourceUrl(doc: DocPage): string {
  return `https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/${doc.sourcePath}`;
}

export function generateStaticParams(): { slug: string[] }[] {
  const docs = getAllDocs();
  return docs
    .filter((doc) => doc.slug !== "") // docs home handled by /docs/page.tsx
    .map((doc) => ({
      slug: doc.slug.split("/"),
    }));
}

/** Canonical HTML URL of a page. */
export function docUrl(slug: string): string {
  return slug === "" ? `${SITE_URL}/docs` : `${SITE_URL}/docs/${slug}`;
}

/** Slug used in the raw-markdown URL. The docs home has an empty slug, so it is served as index.md. */
export function markdownSlug(slug: string): string {
  return slug === "" ? "index" : slug;
}

/** Site-relative path of a page's raw markdown. */
export function markdownPath(slug: string): string {
  return `/docs/${markdownSlug(slug)}.md`;
}

/** Absolute URL of a page's raw markdown. */
export function markdownUrl(slug: string): string {
  return `${SITE_URL}${markdownPath(slug)}`;
}
