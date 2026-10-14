import { getAllDocs } from "@/lib/docs";
import { buildNavTree } from "@/lib/nav-tree";
import DocsLayout from "@/components/docs/DocsLayout";

/**
 * The sidebar lives in the layout rather than in each page, so it stays mounted
 * across navigations and keeps its open groups and scroll position.
 */
export default function DocsSectionLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <DocsLayout navTree={buildNavTree(getAllDocs())}>{children}</DocsLayout>;
}
