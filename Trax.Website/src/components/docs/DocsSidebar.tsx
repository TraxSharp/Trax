"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import type { NavItem, NavSection } from "@/lib/nav-tree";

function NavLink({
  item,
  depth = 0,
}: {
  item: NavItem;
  depth?: number;
}) {
  const pathname = usePathname();
  const href = item.slug === "" ? "/docs" : `/docs/${item.slug}`;
  const isActive = pathname === href;
  const isParentOfActive =
    item.children.length > 0 &&
    (pathname.startsWith(href + "/") || isActive);
  const [isOpen, setIsOpen] = useState(isParentOfActive);

  const hasChildren = item.children.length > 0;

  return (
    <div>
      <div className="flex items-center">
        <Link
          href={href}
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
            aria-label={isOpen ? "Collapse" : "Expand"}
          >
            <svg
              className={`h-3 w-3 transition-transform ${isOpen ? "rotate-90" : ""}`}
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={2}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M9 5l7 7-7 7"
              />
            </svg>
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

export default function DocsSidebar({ navTree }: { navTree: NavSection[] }) {
  return (
    <aside className="w-64 flex-shrink-0 overflow-y-auto border-r border-border bg-bg-secondary p-4">
      <nav>
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
              {section.items.map((item) => (
                <NavLink key={item.slug} item={item} />
              ))}
            </div>
          </div>
        ))}
      </nav>
    </aside>
  );
}
