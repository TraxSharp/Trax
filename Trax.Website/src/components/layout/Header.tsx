"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import MobileNav from "./MobileNav";

const navLinks = [
  { href: "/", label: "Home" },
  { href: "/docs", label: "Docs" },
  { href: "/blog", label: "Blog" },
];

export default function Header() {
  const pathname = usePathname();
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <header className="sticky top-0 z-50 border-b border-border bg-bg-primary/80 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8">
        <Link href="/" className="flex items-center gap-2">
          <span className="text-lg font-bold text-accent">Trax</span>
        </Link>

        <nav className="hidden items-center gap-1 md:flex">
          {navLinks.map((link) => {
            const isActive =
              link.href === "/"
                ? pathname === "/"
                : pathname.startsWith(link.href);
            return (
              <Link
                key={link.href}
                href={link.href}
                className={`rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                  isActive
                    ? "bg-accent-muted text-accent"
                    : "text-text-secondary hover:bg-bg-hover hover:text-text-primary"
                }`}
              >
                {link.label}
              </Link>
            );
          })}
          <a
            href="https://github.com/TraxSharp"
            target="_blank"
            rel="noopener noreferrer"
            className="ml-2 rounded-lg px-3 py-2 text-sm text-text-muted transition-colors hover:text-text-primary"
          >
            GitHub
          </a>
        </nav>

        <button
          onClick={() => setMobileOpen(!mobileOpen)}
          className="rounded-lg p-2 text-text-secondary hover:bg-bg-hover md:hidden"
          aria-label="Toggle navigation"
        >
          <svg
            width="24"
            height="24"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
          >
            {mobileOpen ? (
              <path d="M6 6l12 12M6 18L18 6" />
            ) : (
              <path d="M4 6h16M4 12h16M4 18h16" />
            )}
          </svg>
        </button>
      </div>

      {mobileOpen && (
        <MobileNav
          links={navLinks}
          pathname={pathname}
          onClose={() => setMobileOpen(false)}
        />
      )}
    </header>
  );
}
