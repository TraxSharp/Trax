import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useClient } from "urql";
import { Search as SearchIcon } from "lucide-react";
import { LOOKUP } from "../graphql/queries";
import { LOOKUP_KIND_LABEL, lookupPath } from "../lib/lookup";
import type { LookupMatch } from "../types";

/**
 * The header's search: any id an operator holds (a run's id or external id, a manifest's name, a
 * subject key, an instance's GUID), looked up exactly (operations.lookup). One match opens it;
 * several are listed by kind. "/" focuses it from anywhere on the page.
 */
export function Search() {
  const client = useClient();
  const navigate = useNavigate();
  const input = useRef<HTMLInputElement>(null);
  const [text, setText] = useState("");
  const [matches, setMatches] = useState<LookupMatch[] | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const typing = target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable);
      if (e.key === "/" && !typing) {
        e.preventDefault();
        input.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  function open(m: LookupMatch) {
    setMatches(null);
    setText("");
    navigate(lookupPath(m));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    const q = text.trim();
    if (!q) return;
    setBusy(true);
    const r = await client.query(LOOKUP, { text: q }, { requestPolicy: "network-only" }).toPromise();
    setBusy(false);
    const found: LookupMatch[] = r.data?.operations?.lookup ?? [];
    if (found.length === 1) open(found[0]);
    else setMatches(found);
  }

  return (
    <form role="search" onSubmit={onSubmit} className="relative">
      <SearchIcon className="pointer-events-none absolute left-2 top-1/2 size-3.5 -translate-y-1/2 text-muted" />
      <input
        ref={input}
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          setMatches(null);
        }}
        onKeyDown={(e) => e.key === "Escape" && setMatches(null)}
        aria-label="Find by id, external id, subject or name"
        placeholder="Find by id, external id, subject or name  /"
        className="w-80 rounded-md border border-line bg-field py-1 pl-7 pr-2 text-xs text-fg placeholder:text-faint"
        disabled={busy}
      />
      {matches && (
        <div
          role="listbox"
          aria-label="Matches"
          className="absolute left-0 top-full z-30 mt-1 w-96 rounded-md border border-line bg-surface p-1 shadow-lg"
        >
          {matches.length === 0 ? (
            <p className="px-2 py-1.5 text-xs text-muted">Nothing has that id, key or name.</p>
          ) : (
            matches.map((m) => (
              <button
                key={`${m.kind}:${m.id}:${m.rowId ?? ""}`}
                type="button"
                role="option"
                aria-selected={false}
                onClick={() => open(m)}
                className="flex w-full items-baseline gap-2 rounded px-2 py-1.5 text-left text-xs hover:bg-hover"
              >
                <span className="w-28 shrink-0 text-muted">{LOOKUP_KIND_LABEL[m.kind]}</span>
                <span className="truncate text-fg">{m.label}</span>
              </button>
            ))
          )}
        </div>
      )}
    </form>
  );
}
