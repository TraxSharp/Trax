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
 * back on the left.
 */
export default function DocsLayout({ navTree, children }: DocsLayoutProps) {
  return (
    <div className="flex min-h-[calc(100vh-4rem)]">
      <main className="order-2 flex flex-1 overflow-hidden">
        <article
          className="flex-1 overflow-y-auto px-8 py-8 lg:px-12"
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
