import Link from "next/link";

export default function NotFound() {
  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center px-4">
      <h1 className="text-6xl font-bold text-text-muted">404</h1>
      <p className="mt-4 text-lg text-text-secondary">Page not found</p>
      <Link
        href="/"
        className="mt-8 rounded-lg bg-accent px-6 py-3 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
      >
        Go Home
      </Link>
    </div>
  );
}
