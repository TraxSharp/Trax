import { useCallback, useState } from "react";

// Keyset pagination state: a stack of page-start cursors (afterId). The first page is null.
// Deliberately never exposes an offset `skip` — deep offset paging scans every skipped row.
export function useKeyset() {
  const [cursors, setCursors] = useState<(number | null)[]>([null]);
  const afterId = cursors[cursors.length - 1];
  const isFirstPage = cursors.length === 1;

  const next = useCallback(
    (cursor: number) => setCursors((c) => [...c, cursor]),
    [],
  );
  const prev = useCallback(
    () => setCursors((c) => (c.length > 1 ? c.slice(0, -1) : c)),
    [],
  );
  const reset = useCallback(() => setCursors([null]), []);

  return { afterId, isFirstPage, next, prev, reset };
}
