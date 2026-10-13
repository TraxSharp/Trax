import type { DocPage } from "./docs";
import {
  DEFAULT_AREA,
  SDK_REFERENCE_SECTION,
  areaLabel,
  areaRank,
  byNavOrder,
  resolveParent,
  sectionOf,
  sectionRank,
} from "./doc-meta.ts";

// The sidebar. This file has no runtime imports beyond doc-meta.ts, so the
// tests load it under plain Node.

export interface NavItem {
  kind: "page";
  title: string;
  slug: string;
  navOrder: number;
  children: NavItem[];
}

/** A collapsible, non-link group of a section's top-level pages that share an area. */
export interface NavGroup {
  kind: "group";
  area: string;
  label: string;
  items: NavItem[];
}

export type NavEntry = NavItem | NavGroup;

export interface NavSection {
  /** Null for a standalone page (Home), which sits above every section. */
  label: string | null;
  items: NavEntry[];
}

/** True when the entry is the page at `slug` or holds it at any depth. */
export function containsSlug(entry: NavEntry, slug: string): boolean {
  if (entry.kind === "page" && entry.slug === slug) return true;
  const children = entry.kind === "page" ? entry.children : entry.items;
  return children.some((child) => containsSlug(child, slug));
}

/**
 * Every page under the page it nests in. A page whose `parent` resolves to no
 * page counts as top-level, so it still appears.
 */
function buildForest(docs: DocPage[]): { roots: DocPage[]; children: Map<DocPage, DocPage[]> } {
  const children = new Map<DocPage, DocPage[]>();
  const roots: DocPage[] = [];
  for (const doc of docs) {
    const parent = resolveParent(doc, docs);
    if (parent) children.set(parent, [...(children.get(parent) ?? []), doc]);
    else roots.push(doc);
  }
  return { roots, children };
}

interface SectionPages {
  label: string | null;
  roots: DocPage[];
}

/**
 * Top-level pages by sidebar section, in sidebar order: standalone pages first
 * (Home, then nav_order), then the known sections in SECTION_ORDER, then any
 * other `section` label in the order its first page comes.
 */
function groupIntoSections(roots: DocPage[]): SectionPages[] {
  const standalone: SectionPages[] = [];
  const named = new Map<string, DocPage[]>();
  const sorted = [...roots].sort((a, b) =>
    a.slug === "" ? -1 : b.slug === "" ? 1 : byNavOrder(a, b)
  );
  for (const doc of sorted) {
    const label = sectionOf(doc);
    if (label === undefined) standalone.push({ label: null, roots: [doc] });
    else named.set(label, [...(named.get(label) ?? []), doc]);
  }
  const sections = [...named.keys()]
    .map((label, firstSeen) => ({ label, firstSeen }))
    .sort((a, b) => sectionRank(a.label) - sectionRank(b.label) || a.firstSeen - b.firstSeen)
    .map(({ label }) => ({ label, roots: named.get(label) ?? [] }));
  return [...standalone, ...sections];
}

export function buildNavTree(docs: DocPage[]): NavSection[] {
  const { roots, children } = buildForest(docs);
  const seen = new Set<DocPage>();

  const toItem = (doc: DocPage): NavItem => {
    seen.add(doc);
    return {
      kind: "page",
      title: doc.title,
      slug: doc.slug,
      navOrder: doc.navOrder,
      children: (children.get(doc) ?? [])
        .filter((c) => !seen.has(c))
        .sort(byNavOrder)
        .map(toItem),
    };
  };

  const areaOf = (d: DocPage) => d.area ?? DEFAULT_AREA;

  return groupIntoSections(roots).map(({ label, roots: sectionRoots }) => {
    const areas = [...new Set(sectionRoots.map(areaOf))];
    const grouped = label !== null && label !== SDK_REFERENCE_SECTION && areas.length > 1;
    if (!grouped) return { label, items: sectionRoots.map(toItem) };
    const items: NavGroup[] = areas
      .sort((a, b) => areaRank(a) - areaRank(b) || a.localeCompare(b))
      .map((area) => ({
        kind: "group",
        area,
        label: areaLabel(area),
        items: sectionRoots.filter((d) => areaOf(d) === area).map(toItem),
      }));
    return { label, items };
  });
}

export interface DocsSection {
  label: string;
  /** Pages in reading order: each top-level page followed by its descendants. */
  pages: { doc: DocPage }[];
}

/** The label llms.txt gives the standalone pages, which have no section. */
export const UNGROUPED_SECTION = "Overview";

/**
 * Every page grouped the way the sidebar groups them, in sidebar order, with
 * the standalone pages together under UNGROUPED_SECTION. Area groups are
 * flattened: within a section, pages follow area order, then nav_order.
 */
export function getDocsSections(docs: DocPage[]): DocsSection[] {
  const bySlug = new Map(docs.map((d) => [d.slug, d]));
  const out: DocsSection[] = [];
  const walk = (item: NavItem, into: DocsSection) => {
    const doc = bySlug.get(item.slug);
    if (doc) into.pages.push({ doc });
    item.children.forEach((child) => walk(child, into));
  };
  for (const section of buildNavTree(docs)) {
    const label = section.label ?? UNGROUPED_SECTION;
    let target = out.find((s) => s.label === label);
    if (!target) {
      target = { label, pages: [] };
      out.push(target);
    }
    const into = target;
    for (const entry of section.items) {
      if (entry.kind === "page") walk(entry, into);
      else entry.items.forEach((item) => walk(item, into));
    }
  }
  return out;
}
