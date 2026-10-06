import type { DocPage } from "./docs";

export interface NavItem {
  title: string;
  slug: string;
  navOrder: number;
  children: NavItem[];
}

export interface NavSection {
  label: string | null;
  items: NavItem[];
}

export function buildNavTree(docs: DocPage[]): NavSection[] {
  // Root-level pages: no parent
  const roots = docs
    .filter((d) => !d.parent)
    .sort((a, b) => a.navOrder - b.navOrder)
    .map((d) => ({
      title: d.title,
      slug: d.slug,
      navOrder: d.navOrder,
      section: d.section,
      children: [] as NavItem[],
    }));

  // Second-level pages: have parent, no grand_parent
  const midLevel = docs.filter((d) => d.parent && !d.grandParent);
  for (const doc of midLevel) {
    const parent = roots.find((r) => r.title === doc.parent);
    if (parent) {
      parent.children.push({
        title: doc.title,
        slug: doc.slug,
        navOrder: doc.navOrder,
        children: [],
      });
    }
  }

  // Third-level pages: have grand_parent
  const leafLevel = docs.filter((d) => d.grandParent);
  for (const doc of leafLevel) {
    const grandParent = roots.find((r) => r.title === doc.grandParent);
    if (grandParent) {
      const parent = grandParent.children.find(
        (c) => c.title === doc.parent
      );
      if (parent) {
        parent.children.push({
          title: doc.title,
          slug: doc.slug,
          navOrder: doc.navOrder,
          children: [],
        });
      }
    }
  }

  // Sort children at each level
  for (const root of roots) {
    root.children.sort((a, b) => a.navOrder - b.navOrder);
    for (const child of root.children) {
      child.children.sort((a, b) => a.navOrder - b.navOrder);
    }
  }

  // Group roots into sections
  const sections: NavSection[] = [];
  const sectionMap = new Map<string, NavSection>();

  for (const root of roots) {
    const label = root.section ?? null;
    const key = label ?? "__standalone__";

    if (label === null) {
      // Standalone items (Home, Getting Started) — each gets its own ungrouped section
      // so they appear individually at the top
      sections.push({ label: null, items: [root] });
    } else {
      let section = sectionMap.get(key);
      if (!section) {
        section = { label, items: [] };
        sectionMap.set(key, section);
        sections.push(section);
      }
      section.items.push(root);
    }
  }

  return sections;
}
