// The demos' colours are CSS variables (src/lib/hue.ts), so the light theme can darken them. Appending a hex alpha to
// one (`${color}66`) makes an invalid colour that the browser drops: a border falls back to the text colour and a fill
// disappears. Translucency goes through alpha() instead. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

const files = (dir: string): string[] =>
  readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
    e.isDirectory() ? files(join(dir, e.name)) : /\.tsx?$/.test(e.name) ? [join(dir, e.name)] : [],
  );

test("no demo appends a hex alpha to a colour, which a CSS variable cannot take", () => {
  const offenders: string[] = [];
  for (const file of files("src")) {
    const text = readFileSync(file, "utf8");
    text.split("\n").forEach((line, i) => {
      if (/\$\{[^}]+\}[0-9a-fA-F]{2}[`"']/.test(line) || /toString\(16\)/.test(line)) offenders.push(`${file}:${i + 1}`);
    });
  }
  assert.deepEqual(offenders, []);
});

test("no demo hard-codes a colour the light theme cannot change", () => {
  const allowed = ["src/lib/hue.ts", "src/components/layout/TraxLogo.tsx"];
  const offenders: string[] = [];
  for (const file of files("src").filter((f) => !allowed.includes(f) && !f.startsWith("src/data/"))) {
    readFileSync(file, "utf8")
      .split("\n")
      .forEach((line, i) => {
        if (/#[0-9a-fA-F]{6}\b|rgba?\(/.test(line)) offenders.push(`${file}:${i + 1}`);
      });
  }
  assert.deepEqual(offenders, []);
});
