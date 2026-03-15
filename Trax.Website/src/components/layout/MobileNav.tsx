import Link from "next/link";

interface MobileNavProps {
  links: { href: string; label: string }[];
  pathname: string;
  onClose: () => void;
}

export default function MobileNav({ links, pathname, onClose }: MobileNavProps) {
  return (
    <nav className="border-t border-border bg-bg-secondary p-4 md:hidden">
      <div className="flex flex-col gap-1">
        {links.map((link) => {
          const isActive =
            link.href === "/"
              ? pathname === "/"
              : pathname.startsWith(link.href);
          return (
            <Link
              key={link.href}
              href={link.href}
              onClick={onClose}
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
          className="rounded-lg px-3 py-2 text-sm text-text-muted transition-colors hover:text-text-primary"
        >
          GitHub
        </a>
      </div>
    </nav>
  );
}
