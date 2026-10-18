import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";

/**
 * A list page's filter, held in the URL's query rather than in component state, so a filtered
 * view can be linked (from the Overview, a machine's state, a log line), survives Back and a
 * reload, and can be shared. The default is left out of the URL. Changes replace the history
 * entry rather than push one per keystroke.
 */
// The query this tick's earlier updates produced, until the router has applied them.
let pending: URLSearchParams | null = null;

export function useUrlState<T extends string = string>(key: string, fallback = "" as T): [T, (value: T) => void] {
  const [params, setParams] = useSearchParams();
  const value = (params.get(key) ?? fallback) as T;
  const set = useCallback(
    (next: T) => {
      // Several filters set in one handler (pick a machine and a state) must all land: the router
      // applies each update to the location as it was, not to the one the previous call queued.
      const base = new URLSearchParams(pending ?? params);
      if (next === fallback || next === "") base.delete(key);
      else base.set(key, next);
      if (!pending) queueMicrotask(() => (pending = null));
      pending = base;
      setParams(base, { replace: true });
    },
    [key, fallback, params, setParams],
  );
  return [value, set];
}

/** The same, for a filter whose "none" is null rather than an empty string. */
export function useUrlStateOrNull<T extends string = string>(key: string): [T | null, (value: T | null) => void] {
  const [value, set] = useUrlState<string>(key);
  const setOrNull = useCallback((next: T | null) => set(next ?? ""), [set]);
  return [value === "" ? null : (value as T), setOrNull];
}
