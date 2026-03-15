import Link from "next/link";

interface BlogCardProps {
  slug: string;
  title: string;
  date: string;
  description: string;
  tags: string[];
}

export default function BlogCard({
  slug,
  title,
  date,
  description,
  tags,
}: BlogCardProps) {
  return (
    <Link
      href={`/blog/${slug}`}
      className="group block rounded-lg border border-border bg-bg-card p-6 transition-colors hover:border-border-hover hover:bg-bg-hover"
    >
      <div className="flex items-center gap-2 text-xs text-text-muted">
        <time dateTime={date}>
          {new Date(date).toLocaleDateString("en-US", {
            year: "numeric",
            month: "long",
            day: "numeric",
          })}
        </time>
        {tags.length > 0 && (
          <>
            <span>&middot;</span>
            <div className="flex gap-1">
              {tags.map((tag) => (
                <span
                  key={tag}
                  className="rounded bg-accent-muted px-1.5 py-0.5 text-accent"
                >
                  {tag}
                </span>
              ))}
            </div>
          </>
        )}
      </div>
      <h2 className="mt-2 text-xl font-semibold text-text-primary group-hover:text-accent">
        {title}
      </h2>
      <p className="mt-2 text-sm leading-relaxed text-text-secondary">
        {description}
      </p>
    </Link>
  );
}
