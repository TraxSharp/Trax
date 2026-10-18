import { CircleHelp } from "lucide-react";
import { GLOSSARY, TERMS, type TermKey } from "../lib/vocabulary";

/** A small "?" beside a Trax term: what it means on hover, its glossary entry on click. */
export function TermHelp({ term }: { term: TermKey }) {
  const t = TERMS[term];
  return (
    <a
      href={GLOSSARY}
      target="_blank"
      rel="noreferrer"
      title={t.means}
      aria-label={`What is a ${t.name}? ${t.means}`}
      className="inline-flex align-middle text-muted hover:text-accent-fg"
    >
      <CircleHelp className="size-4" />
    </a>
  );
}
