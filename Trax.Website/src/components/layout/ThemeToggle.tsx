"use client";

import { useSyncExternalStore } from "react";
import { applyTheme, currentTheme, setTheme, subscribeTheme, THEME_KEY, type Theme } from "@/lib/theme";

// The theme lives on <html> (set there before the first paint); this reads it, and follows a change made in the
// framed console demo or another tab through the storage event.
function subscribe(onChange: () => void): () => void {
  const onStorage = (e: StorageEvent) => {
    if (e.key !== THEME_KEY) return;
    applyTheme(e.newValue === "light" ? "light" : "dark");
    onChange();
  };
  window.addEventListener("storage", onStorage);
  const unsubscribe = subscribeTheme(onChange);
  return () => {
    window.removeEventListener("storage", onStorage);
    unsubscribe();
  };
}

/** Switches the site between its dark and light themes. */
export default function ThemeToggle() {
  // Null on the server, which does not know the reader's choice: the button renders without saying which way it goes.
  const theme = useSyncExternalStore<Theme | null>(subscribe, currentTheme, () => null);
  const next: Theme = theme === "light" ? "dark" : "light";
  const label = theme == null ? "Switch theme" : `Switch to the ${next} theme`;
  return (
    <button
      type="button"
      onClick={() => setTheme(next)}
      aria-label={label}
      title={label}
      className="rounded-lg p-2 text-text-muted transition-colors hover:bg-bg-hover hover:text-text-primary"
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden>
        {theme === "light" ? (
          <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
        ) : (
          <>
            <circle cx="12" cy="12" r="4" />
            <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
          </>
        )}
      </svg>
    </button>
  );
}
