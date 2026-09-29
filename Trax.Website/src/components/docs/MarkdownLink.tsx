import { markdownPath } from "@/lib/docs";

/** Link to the page's raw markdown, for readers and tools that want the source. */
export default function MarkdownLink({ slug }: { slug: string }) {
  return (
    <a
      href={markdownPath(slug)}
      type="text/markdown"
      className="shrink-0 rounded-md border border-border px-2 py-0.5 text-xs text-text-muted transition-colors hover:border-accent/40 hover:text-text-secondary"
    >
      View as Markdown
    </a>
  );
}
