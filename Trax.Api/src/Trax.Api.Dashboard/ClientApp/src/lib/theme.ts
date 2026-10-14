// Theme state: light/dark, persisted in localStorage and applied as a `.dark` class on
// <html>. Defaults to the OS preference on first load. Small pub/sub so React can subscribe.

export type Theme = "light" | "dark";

const STORAGE_KEY = "trax-dashboard-theme";
const listeners = new Set<() => void>();

function systemPref(): Theme {
  // The demo is framed in traxsharp.net, which is dark, so it starts dark too (the toggle still works).
  if (import.meta.env.VITE_DEMO === "1") return "dark";
  return window.matchMedia?.("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
}

// localStorage can be absent (SSR / tests) or throw (private browsing), so read/write it
// defensively — the theme still works, it just won't persist.
function readStored(): string | null {
  try {
    return globalThis.localStorage?.getItem(STORAGE_KEY) ?? null;
  } catch {
    return null;
  }
}

function writeStored(theme: Theme) {
  try {
    globalThis.localStorage?.setItem(STORAGE_KEY, theme);
  } catch {
    /* ignore */
  }
}

export function getTheme(): Theme {
  const stored = readStored();
  return stored === "dark" || stored === "light" ? stored : systemPref();
}

function apply(theme: Theme) {
  document.documentElement.classList.toggle("dark", theme === "dark");
}

export function setTheme(theme: Theme) {
  writeStored(theme);
  apply(theme);
  listeners.forEach((fn) => fn());
}

export function toggleTheme() {
  setTheme(getTheme() === "dark" ? "light" : "dark");
}

export function subscribeTheme(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

// Apply the persisted/system theme immediately on module load, before first paint.
apply(getTheme());
