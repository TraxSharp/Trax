import Link from "next/link";

interface DocsBreadcrumbProps {
  title: string;
  parent?: string;
  grandParent?: string;
}

export default function DocsBreadcrumb({
  title,
  parent,
  grandParent,
}: DocsBreadcrumbProps) {
  return (
    <nav className="mb-6 flex items-center gap-2 text-sm text-text-muted">
      <Link href="/docs" className="hover:text-text-secondary">
        Docs
      </Link>
      {grandParent && (
        <>
          <span>/</span>
          <span>{grandParent}</span>
        </>
      )}
      {parent && (
        <>
          <span>/</span>
          <span>{parent}</span>
        </>
      )}
      <span>/</span>
      <span className="text-text-secondary">{title}</span>
    </nav>
  );
}
