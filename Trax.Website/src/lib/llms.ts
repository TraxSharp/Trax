import { getAllDocs, markdownUrl, type DocPage } from "./docs";
import { SITE_DESCRIPTION, SITE_URL } from "./site";

/**
 * Agent-facing views of the docs: the llms.txt index (https://llmstxt.org) and
 * the full-text bundles it links to. Both cover every published page, not only
 * the ones the sidebar reaches.
 */

/** Keep each bundle under common agent fetch limits (~100 KB). */
const BUNDLE_MAX_BYTES = 90_000;

const UNGROUPED_SECTION = "Overview";

export interface DocsSection {
  label: string;
  /** Pages in reading order: each top-level page followed by its descendants. */
  pages: { doc: DocPage }[];
}

export interface Bundle {
  /** File name under /llms-full/, e.g. "guides.txt". */
  file: string;
  title: string;
  pages: DocPage[];
  text: string;
}

function byNavOrder(a: DocPage, b: DocPage): number {
  return a.navOrder - b.navOrder || a.title.localeCompare(b.title);
}

/**
 * Resolves each page's parent from its Jekyll front matter. Titles are not
 * unique, so a candidate whose own parent matches the page's grand_parent wins,
 * then a top-level candidate, then any page with that title.
 */
function resolveParent(doc: DocPage, docs: DocPage[]): DocPage | undefined {
  if (!doc.parent) return undefined;
  const candidates = docs.filter((c) => c !== doc && c.title === doc.parent);
  if (doc.grandParent) {
    const exact = candidates.find((c) => c.parent === doc.grandParent);
    if (exact) return exact;
  }
  return candidates.find((c) => !c.parent) ?? candidates[0];
}

/** Every page grouped the way the sidebar groups them, in sidebar order. */
export function getDocsSections(docs: DocPage[] = getAllDocs()): DocsSection[] {
  const children = new Map<DocPage, DocPage[]>();
  const roots: DocPage[] = [];
  for (const doc of docs) {
    const parent = resolveParent(doc, docs);
    if (parent) {
      children.set(parent, [...(children.get(parent) ?? []), doc]);
    } else {
      roots.push(doc);
    }
  }

  const sections: DocsSection[] = [];
  const byLabel = new Map<string, DocsSection>();
  const visited = new Set<DocPage>();

  const walk = (doc: DocPage, into: DocsSection) => {
    if (visited.has(doc)) return;
    visited.add(doc);
    into.pages.push({ doc });
    for (const child of (children.get(doc) ?? []).sort(byNavOrder)) {
      walk(child, into);
    }
  };

  for (const root of roots.sort(byNavOrder)) {
    const label = root.section ?? UNGROUPED_SECTION;
    let section = byLabel.get(label);
    if (!section) {
      section = { label, pages: [] };
      byLabel.set(label, section);
      sections.push(section);
    }
    walk(root, section);
  }
  return sections;
}

function slugify(text: string): string {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
}

function pageTitle(doc: DocPage): string {
  return doc.slug === "" ? "Home" : doc.title;
}

/** One page as it appears inside a bundle: a title, its source URL, then its markdown. */
function bundleEntry(doc: DocPage): string {
  // Drop the page's own H1 when it repeats the title, so it is not stated twice.
  const body = doc.body
    .replace(/^\s*#\s+(.+)\n/, (line, heading: string) =>
      heading.trim() === doc.title ? "" : line
    )
    .trim();
  return `# ${pageTitle(doc)}\n\nSource: ${markdownUrl(doc.slug)}\n\n${body}\n`;
}

const bytes = (text: string) => Buffer.byteLength(text, "utf8");

function joinEntries(entries: string[]): string {
  return entries.join("\n---\n\n");
}

/**
 * Splits the docs into per-section bundles. A section that fits under the size
 * limit is one bundle; a larger one is split at page boundaries into numbered
 * parts. A single page larger than the limit gets a bundle of its own.
 */
export function getBundles(): Bundle[] {
  const bundles: Bundle[] = [];
  for (const section of getDocsSections()) {
    const base = slugify(section.label);
    const parts: { pages: DocPage[]; entries: string[] }[] = [];
    let current: { pages: DocPage[]; entries: string[] } = {
      pages: [],
      entries: [],
    };
    for (const { doc } of section.pages) {
      const entry = bundleEntry(doc);
      const candidate = joinEntries([...current.entries, entry]);
      if (current.entries.length > 0 && bytes(candidate) > BUNDLE_MAX_BYTES) {
        parts.push(current);
        current = { pages: [], entries: [] };
      }
      current.pages.push(doc);
      current.entries.push(entry);
    }
    if (current.entries.length > 0) parts.push(current);

    parts.forEach((part, i) => {
      const numbered = parts.length > 1;
      bundles.push({
        file: numbered ? `${base}-${i + 1}.txt` : `${base}.txt`,
        title: numbered
          ? `${section.label} (part ${i + 1} of ${parts.length})`
          : section.label,
        pages: part.pages,
        text: joinEntries(part.entries),
      });
    });
  }
  return bundles;
}

export function bundleUrl(bundle: Bundle): string {
  return `${SITE_URL}/llms-full/${bundle.file}`;
}

function formatSize(n: number): string {
  return `${Math.round(n / 1000)} KB`;
}

function describeBundle(bundle: Bundle): string {
  const first = pageTitle(bundle.pages[0]);
  const last = pageTitle(bundle.pages[bundle.pages.length - 1]);
  const span =
    bundle.pages.length === 1 ? first : `${first} through ${last}`;
  return `${span} (${bundle.pages.length} pages, ${formatSize(bytes(bundle.text))})`;
}

/** The /llms.txt index. */
export function buildLlmsTxt(): string {
  const lines: string[] = [
    "# Trax .NET",
    "",
    `> ${SITE_DESCRIPTION} Logic is written as typed pipelines ("trains") of single-purpose steps ("junctions") in which a failure short-circuits the rest; Core, Effect, Mediator, Scheduler, API and Dashboard are separate NuGet packages, each adding one layer.`,
    "",
    `Every page below links to its raw Markdown: append \`.md\` to any docs URL (the docs home is ${markdownUrl("")}). The HTML pages are at the same path without \`.md\`. There is no single llms-full.txt, because the whole corpus is too large for most fetch limits; the full text is split into per-section bundles of at most ~90 KB, listed under "Full text".`,
    "",
  ];

  for (const section of getDocsSections()) {
    lines.push(`## ${section.label}`, "");
    for (const { doc } of section.pages) {
      lines.push(
        `- [${pageTitle(doc)}](${markdownUrl(doc.slug)}): ${doc.description}`
      );
    }
    lines.push("");
  }

  lines.push("## Full text", "");
  for (const bundle of getBundles()) {
    lines.push(
      `- [${bundle.title}](${bundleUrl(bundle)}): ${describeBundle(bundle)}`
    );
  }
  lines.push("");
  return lines.join("\n");
}
