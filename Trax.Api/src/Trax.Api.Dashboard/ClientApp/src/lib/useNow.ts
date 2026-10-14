import { useEffect, useState } from "react";

// The current time, re-read every `intervalMs`. Components that show "how long ago" read it
// here instead of calling Date.now() during render, which keeps render pure and lets the
// relative times move on their own between data refreshes.
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}
