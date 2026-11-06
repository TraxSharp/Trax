import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
// @ts-expect-error: a plain node module with no types
import { formatValidator } from "../tools/conformance/format-schema.mjs";
import { conformanceFile } from "./fixtures";

// The glossary and the host file of machines derived from features (conformance/glossary.schema.json,
// conformance/host.schema.json). Their corpus files each document by what the schema answers: valid and refused
// (whose names disagree, which only the reader can tell) are accepted, invalid is refused. The C# FormatCorpusTests
// holds the C# reader to the same corpus.

const kinds = ["valid", "invalid", "refused"] as const;

describe.each(["glossary", "host"])("the %s schema", (format) => {
  const check = formatValidator(format) as (text: string) => string[];
  const corpus = kinds.flatMap((kind) => {
    const dir = conformanceFile("formats", format, kind);
    return fs
      .readdirSync(dir)
      .filter((f) => f.endsWith(".json"))
      .sort()
      .map((f): [string, string, string] => [kind, f, path.join(dir, f)]);
  });

  it("has documents of every kind", () => {
    expect(new Set(corpus.map(([kind]) => kind))).toEqual(new Set(kinds));
  });

  it.each(corpus)("files %s/%s where it is", (kind, _, file) => {
    const errors = check(fs.readFileSync(file, "utf8"));
    if (kind === "invalid") expect(errors).not.toEqual([]);
    else expect(errors).toEqual([]);
  });
});
