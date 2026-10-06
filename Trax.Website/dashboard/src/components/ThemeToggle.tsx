import { useSyncExternalStore } from "react";
import { getTheme, subscribeTheme, toggleTheme } from "../lib/theme";
import { Moon, Sun } from "lucide-react";

export function ThemeToggle() {
  const theme = useSyncExternalStore(subscribeTheme, getTheme, () => "light");
  const dark = theme === "dark";
  return (
    <button
      onClick={toggleTheme}
      title={dark ? "Switch to light mode" : "Switch to dark mode"}
      className="w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-muted hover:bg-hover"
    >
      {dark ? <Sun className="size-4 shrink-0" /> : <Moon className="size-4 shrink-0" />}
      {dark ? "Light mode" : "Dark mode"}
    </button>
  );
}
