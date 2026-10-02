import { markdownPath, sourceUrl, type DocPage } from "@/lib/docs";

const linkClass =
  "shrink-0 rounded-md border border-border px-2 py-0.5 text-xs text-text-muted transition-colors hover:border-accent/40 hover:text-text-secondary";

/**
 * Links to the page's raw markdown on this site and to its source file in
 * Trax.Docs, for readers and tools that want the source or want to edit it.
 */
export default function PageLinks({ doc }: { doc: DocPage }) {
  return (
    <div className="flex shrink-0 gap-2">
      <a href={markdownPath(doc.slug)} type="text/markdown" className={linkClass}>
        View as Markdown
      </a>
      <a
        href={sourceUrl(doc)}
        target="_blank"
        rel="noopener noreferrer"
        className={linkClass}
      >
        Edit on GitHub
      </a>
    </div>
  );
}
