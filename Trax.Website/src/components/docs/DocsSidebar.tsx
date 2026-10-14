"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import {
  containsSlug,
  type NavEntry,
  type NavGroup,
  type NavItem,
  type NavSection,
} from "@/lib/nav-tree";

function slugOf(pathname: string): string {
  return pathname.replace(/^\/docs\/?/, "").replace(/\/$/, "");
}

/**
 * Open state for a collapsible entry: it starts open when it holds the current
 * page, and opens again when the reader navigates into it.
 */
function useOpenFor(entry: NavEntry, pathname: string): [boolean, (open: boolean) => void] {
  const holdsCurrent = containsSlug(entry, slugOf(pathname));
  const [isOpen, setIsOpen] = useState(holdsCurrent);
  const [seenPath, setSeenPath] = useState(pathname);
  if (seenPath !== pathname) {
    setSeenPath(pathname);
    if (holdsCurrent && !isOpen) setIsOpen(true);
  }
  return [isOpen, setIsOpen];
}

function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      className={`h-3 w-3 shrink-0 transition-transform ${open ? "rotate-90" : ""}`}
      fill="none"
      viewBox="0 0 24 24"
      stroke="currentColor"
      strokeWidth={2}
      aria-hidden="true"
    >
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
    </svg>
  );
}

function NavLink({ item, depth = 0 }: { item: NavItem; depth?: number }) {
  const pathname = usePathname();
  const href = item.slug === "" ? "/docs" : `/docs/${item.slug}`;
  const isActive = pathname === href;
  const [isOpen, setIsOpen] = useOpenFor(item, pathname);
  const hasChildren = item.children.length > 0;

  return (
    <div>
      <div className="flex items-center">
        <Link
          href={href}
          aria-current={isActive ? "page" : undefined}
          className={`flex-1 rounded-md px-3 py-1.5 text-sm transition-colors ${
            isActive
              ? "bg-accent-muted font-medium text-accent"
              : "text-text-secondary hover:bg-bg-hover hover:text-text-primary"
          }`}
          style={{ paddingLeft: `${depth * 12 + 12}px` }}
        >
          {item.title}
        </Link>
        {hasChildren && (
          <button
            onClick={() => setIsOpen(!isOpen)}
            className="mr-1 rounded p-1 text-text-muted hover:text-text-secondary"
            aria-label={`${isOpen ? "Collapse" : "Expand"} ${item.title}`}
            aria-expanded={isOpen}
          >
            <Chevron open={isOpen} />
          </button>
        )}
      </div>
      {hasChildren && isOpen && (
        <div className="mt-0.5">
          {item.children.map((child) => (
            <NavLink key={child.slug} item={child} depth={depth + 1} />
          ))}
        </div>
      )}
    </div>
  );
}

/** An area inside a section: a button that shows or hides its pages, not a link. */
function NavGroupBlock({ group }: { group: NavGroup }) {
  const pathname = usePathname();
  const [isOpen, setIsOpen] = useOpenFor(group, pathname);
  return (
    <div>
      <button
        onClick={() => setIsOpen(!isOpen)}
        aria-expanded={isOpen}
        className="flex w-full items-center gap-1.5 rounded-md px-3 py-1.5 text-left text-sm font-medium text-text-primary transition-colors hover:bg-bg-hover"
      >
        <Chevron open={isOpen} />
        {group.label}
      </button>
      {isOpen && (
        <div className="mt-0.5 space-y-0.5">
          {group.items.map((item) => (
            <NavLink key={item.slug} item={item} depth={1} />
          ))}
        </div>
      )}
    </div>
  );
}

function Entry({ entry }: { entry: NavEntry }) {
  return entry.kind === "group" ? <NavGroupBlock group={entry} /> : <NavLink item={entry} />;
}

export default function DocsSidebar({ navTree }: { navTree: NavSection[] }) {
  const pathname = usePathname();
  const asideRef = useRef<HTMLElement>(null);

  // Bring the current page's link into view when it is outside the sidebar's
  // visible part, as on a direct load of a deep page. A link the reader just
  // clicked is already visible, so this leaves the scroll alone.
  useEffect(() => {
    const aside = asideRef.current;
    const active = aside?.querySelector<HTMLElement>('[aria-current="page"]');
    if (!aside || !active) return;
    const a = aside.getBoundingClientRect();
    const l = active.getBoundingClientRect();
    if (l.top < a.top || l.bottom > a.bottom) {
      active.scrollIntoView({ block: "center" });
    }
  }, [pathname]);

  return (
    <aside
      ref={asideRef}
      className="sticky top-16 order-1 h-[calc(100vh-4rem)] w-64 flex-shrink-0 self-start overflow-y-auto overscroll-contain border-r border-border bg-bg-secondary p-4"
    >
      <nav aria-label="Documentation">
        {navTree.map((section, i) => (
          <div
            key={section.label ?? `standalone-${i}`}
            className={
              section.label === "SDK Reference"
                ? "mt-4 border-t border-border pt-4"
                : section.label
                  ? "mt-4"
                  : ""
            }
          >
            {section.label && (
              <p className="mb-1 px-3 text-[11px] font-semibold uppercase tracking-wider text-text-muted">
                {section.label}
              </p>
            )}
            <div className="space-y-0.5">
              {section.items.map((entry) => (
                <Entry
                  key={entry.kind === "group" ? `group-${entry.area}` : entry.slug}
                  entry={entry}
                />
              ))}
            </div>
          </div>
        ))}
      </nav>
    </aside>
  );
}
