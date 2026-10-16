import type { DocPage } from "./docs";

// The docs' Diátaxis vocabulary (types, areas, sections) and the helpers the
// sidebar, the page chrome and llms.txt share. Plain functions over DocPage
// with no runtime imports, so the tests load this file under plain Node.

export const GITHUB_REPO = "https://github.com/TraxSharp/Trax";

export const DOC_TYPES = [
  "tutorial",
  "how-to",
  "explanation",
  "reference",
  "landing",
] as const;

export type DocType = (typeof DOC_TYPES)[number];

export function isDocType(value: unknown): value is DocType {
  return typeof value === "string" && (DOC_TYPES as readonly string[]).includes(value);
}

/** The badge above a page's H1. Landing pages carry none. */
export const TYPE_LABELS: Partial<Record<DocType, string>> = {
  tutorial: "Tutorial",
  "how-to": "How-to guide",
  explanation: "Explanation",
  reference: "Reference",
};

/** The sidebar section a type implies, and the heading of its group in "More on". */
export const TYPE_SECTIONS: Partial<Record<DocType, string>> = {
  tutorial: "Tutorials",
  "how-to": "How-to guides",
  explanation: "Explanation",
  reference: "Reference",
};

/** The types "More on <area>" lists, in the order it lists them. */
export const MORE_ON_TYPES: DocType[] = ["tutorial", "how-to", "explanation", "reference"];

export const SDK_REFERENCE_SECTION = "SDK Reference";

/** Sidebar sections, in order. Standalone pages (Home) come before all of them. */
export const SECTION_ORDER = [
  "Tutorials",
  "How-to guides",
  "Explanation",
  "Reference",
  SDK_REFERENCE_SECTION,
  "Samples",
  "Contributing",
];

/** Areas in sidebar order, with their labels. */
export const AREAS: { id: string; label: string }[] = [
  { id: "general", label: "General" },
  { id: "trains", label: "Trains" },
  { id: "effects", label: "Effects" },
  { id: "data", label: "Data" },
  { id: "mediator", label: "Mediator" },
  { id: "scheduling", label: "Scheduling" },
  { id: "api", label: "GraphQL API" },
  { id: "security", label: "Security" },
  { id: "machines", label: "State machines" },
  { id: "dashboard", label: "Dashboard" },
  { id: "testing", label: "Testing" },
  { id: "tooling", label: "Tooling" },
  { id: "upgrading", label: "Upgrading" },
  { id: "samples", label: "Samples" },
  { id: "contributing", label: "Contributing" },
];

/** The area a page without one is filed under in the sidebar. */
export const DEFAULT_AREA = "general";

/** Slug of the page that explains the four page types; the type badge links to it. */
export const ABOUT_DOCS_SLUG = "about-these-docs";

export function areaLabel(area: string): string {
  const known = AREAS.find((a) => a.id === area);
  if (known) return known.label;
  return area.charAt(0).toUpperCase() + area.slice(1).replace(/-/g, " ");
}

/** Index of an area in sidebar order; an unknown area sorts after every known one. */
export function areaRank(area: string): number {
  const i = AREAS.findIndex((a) => a.id === area);
  return i === -1 ? AREAS.length : i;
}

/** Index of a section in sidebar order; an unknown section sorts after every known one. */
export function sectionRank(label: string): number {
  const i = SECTION_ORDER.indexOf(label);
  return i === -1 ? SECTION_ORDER.length : i;
}

/**
 * The sidebar section of a top-level page: its explicit `section`, else Samples
 * or Contributing by area, else the one its type implies. Undefined means the
 * page stands alone at the top (Home, a page with `section: none`, and any page
 * with none of these).
 */
export function sectionOf(doc: DocPage): string | undefined {
  if (doc.section === "none") return undefined;
  if (doc.section) return doc.section;
  if (doc.area === "samples") return "Samples";
  if (doc.area === "contributing") return "Contributing";
  return doc.type ? TYPE_SECTIONS[doc.type] : undefined;
}

/** Sidebar order inside a group: nav_order, then title. */
export function byNavOrder(a: DocPage, b: DocPage): number {
  return a.navOrder - b.navOrder || a.title.localeCompare(b.title);
}

/**
 * The page a page's `parent` names. Titles are not unique, so a candidate whose
 * own parent matches the page's grand_parent wins, then a top-level candidate,
 * then any page with that title.
 */
export function resolveParent(doc: DocPage, docs: DocPage[]): DocPage | undefined {
  if (!doc.parent) return undefined;
  const candidates = docs.filter((c) => c !== doc && c.title === doc.parent);
  if (doc.grandParent) {
    const exact = candidates.find((c) => c.parent === doc.grandParent);
    if (exact) return exact;
  }
  return candidates.find((c) => !c.parent) ?? candidates[0];
}

export function asList(value: unknown): string[] {
  if (typeof value === "string") return value.trim() ? [value.trim()] : [];
  if (Array.isArray(value)) {
    return value
      .filter((v): v is string => typeof v === "string")
      .map((v) => v.trim())
      .filter(Boolean);
  }
  return [];
}

/** Whether a `source` path names a file or a directory, as scripts/docs-index.mjs found it. */
export type SourceKinds = Record<string, "file" | "dir" | "missing">;

/** A repository path on GitHub at `ref`: blob for a file, tree for a directory. */
export function githubPathUrl(repoPath: string, ref: string, kind: "file" | "dir"): string {
  const clean = repoPath.replace(/^\/+/, "").replace(/\/+$/, "");
  return `${GITHUB_REPO}/${kind === "dir" ? "tree" : "blob"}/${ref}/${clean}`;
}

export interface SourceLink {
  path: string;
  /** What the link reads: the file or folder name. */
  label: string;
  url: string;
}

/**
 * The page's `source` entries as GitHub links. The kind comes from the sync's
 * look at the checkout; a path it did not see is a directory when it ends in
 * a slash, else a file.
 */
export function sourceLinks(doc: DocPage, ref: string, kinds: SourceKinds = {}): SourceLink[] {
  return (doc.source ?? []).map((p) => {
    const kind = kinds[p] === "dir" || (!kinds[p] && p.endsWith("/")) ? "dir" : "file";
    const name = p.replace(/\/+$/, "").split("/").pop() ?? p;
    return { path: p, label: kind === "dir" ? `${name}/` : name, url: githubPathUrl(p, ref, kind) };
  });
}

/** The package's gallery page: npm for a scoped name, else nuget.org. */
export function packageUrl(pkg: string): string {
  return pkg.startsWith("@")
    ? `https://www.npmjs.com/package/${pkg}`
    : `https://www.nuget.org/packages/${pkg}`;
}

export const SURFACE_LABELS: Record<string, string> = {
  graphql: "GraphQL schema",
  typescript: "TypeScript",
};

export interface MoreOnGroup {
  type: DocType;
  label: string;
  pages: DocPage[];
}

export interface MoreOn {
  areaLabel: string;
  groups: MoreOnGroup[];
}

export const MORE_ON_LIMIT = 6;

/**
 * "More on <area>" at the foot of a page: the other typed pages of its area,
 * grouped by type, at most MORE_ON_LIMIT each. Within a type, SDK reference
 * pages come after the rest so the area's own reference pages are not crowded
 * out. Landing pages and pages without an area get none.
 */
export function moreOnArea(doc: DocPage, docs: DocPage[]): MoreOn | undefined {
  if (!doc.area || doc.type === "landing") return undefined;
  const sdk = (d: DocPage) => (d.slug.startsWith("sdk-reference/") ? 1 : 0);
  const groups: MoreOnGroup[] = [];
  for (const type of MORE_ON_TYPES) {
    const pages = docs
      .filter((d) => d !== doc && d.slug !== doc.slug && d.area === doc.area && d.type === type)
      .sort((a, b) => sdk(a) - sdk(b) || byNavOrder(a, b))
      .slice(0, MORE_ON_LIMIT);
    if (pages.length > 0) groups.push({ type, label: TYPE_SECTIONS[type] ?? type, pages });
  }
  return groups.length > 0 ? { areaLabel: areaLabel(doc.area), groups } : undefined;
}

/**
 * Splits off the page's leading `# Title` line, so the chrome can sit between
 * the H1 and the body. Null when the page does not open with an ATX H1.
 */
export function splitLeadingH1(body: string): { heading: string; rest: string } | null {
  const match = /^\s*(#[ \t]+[^\n]*)(?:\n|$)/.exec(body);
  if (!match) return null;
  return { heading: match[1], rest: body.slice(match[0].length) };
}

/** The href of a docs page. */
export function docHref(slug: string): string {
  return slug === "" ? "/docs" : `/docs/${slug}`;
}
