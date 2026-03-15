import DocsSidebar from "./DocsSidebar";
import TableOfContents from "./TableOfContents";
import type { NavSection } from "@/lib/nav-tree";

interface DocsLayoutProps {
  navTree: NavSection[];
  children: React.ReactNode;
}

export default function DocsLayout({ navTree, children }: DocsLayoutProps) {
  return (
    <div className="flex min-h-[calc(100vh-4rem)]">
      <DocsSidebar navTree={navTree} />
      <div className="flex flex-1 overflow-hidden">
        <article
          className="flex-1 overflow-y-auto px-8 py-8 lg:px-12"
          data-docs-content
        >
          <div className="mx-auto max-w-3xl">{children}</div>
        </article>
        <TableOfContents />
      </div>
    </div>
  );
}
