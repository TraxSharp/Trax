import DocsSidebar from "./DocsSidebar";
import TableOfContents from "./TableOfContents";
import type { NavSection } from "@/lib/nav-tree";

interface DocsLayoutProps {
  navTree: NavSection[];
  children: React.ReactNode;
}

/**
 * The page comes before the sidebar in the DOM, so a reader or tool that takes
 * the document in order reaches the content first; `order` puts the sidebar
 * back on the left. The sidebar sticks below the header and scrolls on its own,
 * so moving through the page does not move the sidebar, and the reverse.
 */
export default function DocsLayout({ navTree, children }: DocsLayoutProps) {
  return (
    <div className="flex min-h-[calc(100vh-4rem)]">
      <main className="order-2 flex min-w-0 flex-1">
        <article
          className="min-w-0 flex-1 overflow-x-auto px-8 py-8 lg:px-12"
          data-docs-content
        >
          <div className="mx-auto max-w-3xl">{children}</div>
        </article>
        <TableOfContents />
      </main>
      <DocsSidebar navTree={navTree} />
    </div>
  );
}
