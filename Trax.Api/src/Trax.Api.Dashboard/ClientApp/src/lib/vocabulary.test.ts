import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, test } from "vitest";
import { GLOSSARY, TERMS } from "./vocabulary";

// api/0043: the console speaks one vocabulary. A run is a run; one verb per action; no C# API
// in page text. This refuses the words the console used before it settled them; it cannot show a
// new button chose the right verb, which review does.
const REFUSED: [RegExp, string][] = [
  [/\bRe-queue/i, "Requeue"],
  [/>\s*Executions?\b|"Executions?\b|`Executions?\b|\bexecution\(s\)/, "run / runs"],
  [/\bMetadata Id\b/i, "run id"],
  [/\bPersisted ops\b/, "Persisted operations"],
  [/AddJunctionEvents\(\)|<code>QueueSubjectKey<\/code>|OnQueue hook|UsePersistedOperations\(/, "plain words and a docs link"],
  [/>\s*Trigger( selected| group)?\s*</, "Queue"],
  [/Hide admin trains/, "Hide scheduler trains"],
];

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return name === "mock" || name === "demo" || name === "e2e" ? [] : sources(path);
    return name.endsWith(".tsx") && !/\.(test|stories)\.tsx$/.test(name) ? [path] : [];
  });
}

describe("console vocabulary", () => {
  test("page text uses none of the refused words", () => {
    const found: string[] = [];
    for (const file of sources(join(__dirname, ".."))) {
      const lines = readFileSync(file, "utf8").split("\n");
      lines.forEach((line, i) => {
        if (/^\s*(\/\/|\*|\/\*)/.test(line)) return; // comments are for developers
        for (const [word, instead] of REFUSED)
          if (word.test(line)) found.push(`${file.split("/ClientApp/")[1]}:${i + 1} use ${instead}: ${line.trim()}`);
      });
    }
    expect(found).toEqual([]);
  });

  // The anchor is the glossary heading's id (Trax.Docs reference/glossary.md); the docs build checks
  // each link lands on a heading (CodeLinksToDocsResolveTests).
  test("every term links its own glossary entry", () => {
    for (const t of Object.values(TERMS)) expect(t.glossary).toMatch(new RegExp(`^${GLOSSARY}#[a-z0-9-]+$`));
    const anchors = Object.values(TERMS).map((t) => t.glossary);
    expect(new Set(anchors).size).toBe(anchors.length);
  });

  test("every term has a one-sentence meaning", () => {
    for (const t of Object.values(TERMS)) expect(t.means).toMatch(/^[A-Z].*\.$/);
  });
});
