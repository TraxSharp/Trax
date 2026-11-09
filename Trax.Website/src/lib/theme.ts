// The site's light and dark themes. Dark is the default; a reader's choice is kept in localStorage under the
// operations console's key, so the console demo framed at /dashboard opens in the same theme, and a change in either
// reaches the other through the storage event. The choice is applied as data-theme on <html> by THEME_SCRIPT
// before the first paint, so a light reader never sees the dark page flash first.

export type Theme = "light" | "dark";

/** The console's own key (Trax.Api.Dashboard ClientApp src/lib/theme.ts). */
export const THEME_KEY = "trax-dashboard-theme";

/** Runs in <head> before anything paints. */
export const THEME_SCRIPT = `try{if(localStorage.getItem(${JSON.stringify(THEME_KEY)})==="light")document.documentElement.dataset.theme="light"}catch(e){}`;

export function currentTheme(): Theme {
  return typeof document !== "undefined" && document.documentElement.dataset.theme === "light" ? "light" : "dark";
}

export function applyTheme(theme: Theme): void {
  if (theme === "light") document.documentElement.dataset.theme = "light";
  else delete document.documentElement.dataset.theme;
}

const listeners = new Set<() => void>();

/** Calls `fn` whenever this document's theme is set. */
export function subscribeTheme(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

export function setTheme(theme: Theme): void {
  try {
    localStorage.setItem(THEME_KEY, theme);
  } catch {
    // Private browsing: the theme still changes, it just is not kept.
  }
  applyTheme(theme);
  listeners.forEach((fn) => fn());
}
