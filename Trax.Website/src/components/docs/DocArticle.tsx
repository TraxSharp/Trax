import Link from "next/link";
import { MDXRemote } from "next-mdx-remote/rsc";
import { siteRef, sourceKinds, type DocPage } from "@/lib/docs";
import {
  ABOUT_DOCS_SLUG,
  SURFACE_LABELS,
  TYPE_LABELS,
  asList,
  docHref,
  moreOnArea,
  packageUrl,
  sourceLinks,
  splitLeadingH1,
} from "@/lib/doc-meta";
import { docsMdxOptions } from "@/lib/mdx-options";
import { mdxComponents } from "@/components/mdx/MdxComponents";

const externalLink =
  "text-accent underline decoration-accent/30 transition-colors hover:text-accent-hover hover:decoration-accent";

/** The page's Diátaxis type, above the H1, linked to the page that explains the types. */
function TypeBadge({ doc, docs }: { doc: DocPage; docs: DocPage[] }) {
  const label = doc.type ? TYPE_LABELS[doc.type] : undefined;
  if (!label) return null;
  const className =
    "inline-block rounded-full border border-accent/40 bg-accent-muted px-2.5 py-0.5 text-xs font-medium text-accent-bright";
  const about = docs.some((d) => d.slug === ABOUT_DOCS_SLUG);
  return (
    <p className="mb-3">
      {about ? (
        <Link
          href={docHref(ABOUT_DOCS_SLUG)}
          className={`${className} transition-colors hover:border-accent`}
          title="About the page types in these docs"
        >
          {label}
        </Link>
      ) : (
        <span className={className}>{label}</span>
      )}
    </p>
  );
}

function Separator() {
  return (
    <span aria-hidden="true" className="text-border-hover">
      ·
    </span>
  );
}

/**
 * The line under the H1: Package, Namespace and Surface on SDK reference
 * pages, and the Source links on any page that lists `source`.
 */
function PageMeta({ doc }: { doc: DocPage }) {
  const sources = sourceLinks(doc, siteRef(), sourceKinds());
  const namespaces = asList(doc.namespace);
  const surface = doc.surface ? (SURFACE_LABELS[doc.surface] ?? doc.surface) : undefined;
  const parts: React.ReactNode[] = [];

  if (doc.package) {
    parts.push(
      <span key="package">
        Package{" "}
        <a href={packageUrl(doc.package)} target="_blank" rel="noopener noreferrer" className={externalLink}>
          {doc.package}
        </a>
      </span>
    );
  }
  if (namespaces.length > 0) {
    parts.push(
      <span key="namespace">
        {namespaces.length > 1 ? "Namespaces" : "Namespace"}{" "}
        {namespaces.map((ns, i) => (
          <span key={ns}>
            {i > 0 && ", "}
            <code className="font-mono text-text-secondary">{ns}</code>
          </span>
        ))}
      </span>
    );
  }
  if (surface && namespaces.length === 0) {
    parts.push(<span key="surface">Surface {surface}</span>);
  }
  if (sources.length > 0) {
    parts.push(
      <span key="source">
        Source{" "}
        {sources.map((s, i) => (
          <span key={s.path}>
            {i > 0 && ", "}
            <a
              href={s.url}
              target="_blank"
              rel="noopener noreferrer"
              title={s.path}
              aria-label={`Source: ${s.path}`}
              className={`${externalLink} font-mono`}
            >
              {s.label}
            </a>
          </span>
        ))}
      </span>
    );
  }
  if (parts.length === 0) return null;

  return (
    <p className="-mt-3 mb-6 flex flex-wrap items-baseline gap-x-2 gap-y-1 text-sm text-text-muted">
      {parts.flatMap((part, i) => (i === 0 ? [part] : [<Separator key={`sep-${i}`} />, part]))}
    </p>
  );
}

/** "More on <area>": the area's other pages by type. Not on landing pages. */
function MoreOnArea({ doc, docs }: { doc: DocPage; docs: DocPage[] }) {
  const more = moreOnArea(doc, docs);
  if (!more) return null;
  return (
    <aside
      aria-labelledby="more-on-area"
      data-toc-exclude
      className="mt-12 border-t border-border pt-8"
    >
      <h2 id="more-on-area" className="mb-4 text-lg font-semibold text-text-primary">
        More on {more.areaLabel}
      </h2>
      <div className="grid gap-6 sm:grid-cols-2">
        {more.groups.map((group) => (
          <div key={group.type}>
            <h3 className="mb-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">
              {group.label}
            </h3>
            <ul className="space-y-1">
              {group.pages.map((page) => (
                <li key={page.slug}>
                  <Link
                    href={docHref(page.slug)}
                    className="text-sm text-text-secondary transition-colors hover:text-accent"
                  >
                    {page.title}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </aside>
  );
}

function Markdown({ source }: { source: string }) {
  return <MDXRemote source={source} components={mdxComponents} options={docsMdxOptions} />;
}

/**
 * A docs page: its type badge, its H1, the Package / Namespace / Source line,
 * the rest of its body, and "More on <area>". A page with none of the new
 * front matter renders as its markdown alone.
 */
export default function DocArticle({ doc, docs }: { doc: DocPage; docs: DocPage[] }) {
  // The H1 is rendered apart from the body only when the meta line goes under it.
  const hasMeta =
    Boolean(doc.package || doc.surface) ||
    asList(doc.namespace).length > 0 ||
    (doc.source ?? []).length > 0;
  const split = hasMeta ? splitLeadingH1(doc.body) : null;
  return (
    <>
      <div className="docs-content">
        <TypeBadge doc={doc} docs={docs} />
        {split ? (
          <>
            <Markdown source={split.heading} />
            <PageMeta doc={doc} />
            <Markdown source={split.rest} />
          </>
        ) : (
          <>
            <PageMeta doc={doc} />
            <Markdown source={doc.body} />
          </>
        )}
      </div>
      <MoreOnArea doc={doc} docs={docs} />
    </>
  );
}
