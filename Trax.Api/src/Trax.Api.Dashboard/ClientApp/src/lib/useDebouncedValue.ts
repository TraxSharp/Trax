import { useEffect, useState } from "react";

/** The value, once it has stopped changing for `ms`. Text filters use it so typing a search term
 * sends one query rather than one per keystroke. */
export function useDebouncedValue<T>(value: T, ms = 300): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return settled;
}
