import type { MDXComponents } from "mdx/types";
import Link from "next/link";
import type { ComponentPropsWithoutRef } from "react";

function isInternalLink(href: string | undefined): boolean {
  if (!href) return false;
  return href.startsWith("/") || href.startsWith("#");
}

export const mdxComponents: MDXComponents = {
  h1: (props: ComponentPropsWithoutRef<"h1">) => (
    <h1
      className="mb-6 text-3xl font-bold tracking-tight text-text-primary"
      {...props}
    />
  ),
  h2: (props: ComponentPropsWithoutRef<"h2">) => (
    <h2
      className="mb-4 mt-10 text-2xl font-semibold text-text-primary"
      {...props}
    />
  ),
  h3: (props: ComponentPropsWithoutRef<"h3">) => (
    <h3
      className="mb-3 mt-8 text-xl font-semibold text-text-primary"
      {...props}
    />
  ),
  h4: (props: ComponentPropsWithoutRef<"h4">) => (
    <h4
      className="mb-2 mt-6 text-lg font-medium text-text-primary"
      {...props}
    />
  ),
  p: (props: ComponentPropsWithoutRef<"p">) => (
    <p className="mb-4 leading-relaxed text-text-secondary" {...props} />
  ),
  a: ({
    href,
    ...props
  }: ComponentPropsWithoutRef<"a">) => {
    if (isInternalLink(href)) {
      return (
        <Link
          href={href || "#"}
          className="text-accent underline decoration-accent/30 transition-colors hover:text-accent-hover hover:decoration-accent"
          {...props}
        />
      );
    }
    return (
      <a
        href={href}
        target="_blank"
        rel="noopener noreferrer"
        className="text-accent underline decoration-accent/30 transition-colors hover:text-accent-hover hover:decoration-accent"
        {...props}
      />
    );
  },
  ul: (props: ComponentPropsWithoutRef<"ul">) => (
    <ul className="mb-4 list-disc space-y-1 pl-6 text-text-secondary" {...props} />
  ),
  ol: (props: ComponentPropsWithoutRef<"ol">) => (
    <ol
      className="mb-4 list-decimal space-y-1 pl-6 text-text-secondary"
      {...props}
    />
  ),
  li: (props: ComponentPropsWithoutRef<"li">) => (
    <li className="leading-relaxed" {...props} />
  ),
  blockquote: (props: ComponentPropsWithoutRef<"blockquote">) => (
    <blockquote
      className="mb-4 border-l-4 border-accent-muted pl-4 italic text-text-muted"
      {...props}
    />
  ),
  code: (props: ComponentPropsWithoutRef<"code">) => {
    // Inline code (not inside a pre)
    const isBlock =
      typeof props.className === "string" &&
      props.className.includes("language-");
    if (isBlock) {
      return <code {...props} />;
    }
    return (
      <code
        className="rounded bg-bg-tertiary px-1.5 py-0.5 font-mono text-sm text-accent"
        {...props}
      />
    );
  },
  pre: (props: ComponentPropsWithoutRef<"pre">) => (
    <pre
      className="mb-4 overflow-x-auto rounded-lg border border-border bg-bg-secondary p-4 text-sm leading-relaxed"
      {...props}
    />
  ),
  table: (props: ComponentPropsWithoutRef<"table">) => (
    <div className="mb-4 overflow-x-auto rounded-lg border border-border">
      <table className="w-full text-left text-sm" {...props} />
    </div>
  ),
  thead: (props: ComponentPropsWithoutRef<"thead">) => (
    <thead className="border-b border-border bg-bg-secondary" {...props} />
  ),
  th: (props: ComponentPropsWithoutRef<"th">) => (
    <th className="px-4 py-2 font-medium text-text-muted" {...props} />
  ),
  td: (props: ComponentPropsWithoutRef<"td">) => (
    <td className="px-4 py-2 text-text-secondary" {...props} />
  ),
  tr: (props: ComponentPropsWithoutRef<"tr">) => (
    <tr className="border-b border-border last:border-0" {...props} />
  ),
  hr: () => <hr className="my-8 border-border" />,
  strong: (props: ComponentPropsWithoutRef<"strong">) => (
    <strong className="font-semibold text-text-primary" {...props} />
  ),
  img: (props: ComponentPropsWithoutRef<"img">) => (
    // eslint-disable-next-line @next/next/no-img-element
    <img className="my-4 rounded-lg border border-border" alt="" {...props} />
  ),
};
