import { getAllDocs, markdownUrl, type DocPage } from "./docs";
import { getDocsSections as navSections, type DocsSection } from "./nav-tree";
import { TYPE_LABELS } from "./doc-meta";
import { SITE_DESCRIPTION, SITE_URL } from "./site";

export type { DocsSection };

/**
 * Agent-facing views of the docs: the llms.txt index (https://llmstxt.org) and
 * the full-text bundles it links to. Both cover every published page, not only
 * the ones the sidebar reaches.
 */

/** Keep each bundle under common agent fetch limits (~100 KB). */
const BUNDLE_MAX_BYTES = 90_000;

export interface Bundle {
  /** File name under /llms-full/, e.g. "how-to-guides.txt". */
  file: string;
  title: string;
  pages: DocPage[];
  text: string;
}

/** Sidebar order, standalone pages first under "Overview" (nav-tree.ts). */
export function getDocsSections(docs: DocPage[] = getAllDocs()): DocsSection[] {
  return navSections(docs);
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
      const type = doc.type ? TYPE_LABELS[doc.type] : undefined;
      lines.push(
        `- [${pageTitle(doc)}](${markdownUrl(doc.slug)}): ${type ? `(${type}) ` : ""}${doc.description}`
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
