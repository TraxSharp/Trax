import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, test } from "vitest";

// The console asks through its own dialog (lib/confirm.ts) and reports through toasts
// (lib/toast.ts), never through the browser's confirm, alert or prompt boxes, which name nothing,
// cannot be styled or tested, and block the page.
function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return sources(path);
    return /\.(ts|tsx)$/.test(name) && !/\.(test|stories)\.tsx?$/.test(name) ? [path] : [];
  });
}

describe("native dialogs", () => {
  test("no page calls window.confirm, alert or prompt", () => {
    const offenders = sources(join(__dirname, "..")).filter((f) =>
      /(^|[^.\w])(window\.)?(confirm|alert|prompt)\(/m.test(readFileSync(f, "utf8")),
    );
    expect(offenders, "use confirmAction (lib/confirm.ts) or toast (lib/toast.ts)").toEqual([]);
  });
});
