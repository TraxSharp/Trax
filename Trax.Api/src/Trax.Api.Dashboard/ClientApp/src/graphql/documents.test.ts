import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { consoleDocuments, consoleDocumentsFile } from "./documents";
import * as mutations from "./mutations";
import * as queries from "./queries";
import * as subscriptions from "./subscriptions";

// The app's own source: what a page can send. Tests, stories, the mock and the demo harness are
// left out, since a document only they use is one no operator sends.
function appSource(): string {
  const src = resolve(process.cwd(), "src");
  return (readdirSync(src, { recursive: true }) as string[])
    .filter((f) => /\.tsx?$/.test(f) && !/\.(test|stories)\.tsx?$/.test(f))
    .filter((f) => !/^(graphql|mock|demo)[\\/]/.test(f))
    .map((f) => readFileSync(join(src, f), "utf8"))
    .join("\n");
}

describe("console documents", () => {
  it("the embedded file is the documents the console sends", () => {
    // Vitest runs from ClientApp, where the file sits.
    const path = resolve(process.cwd(), "console-documents.json");
    // Out of date: run `npm run documents`, which writes it from src/graphql.
    expect(readFileSync(path, "utf8")).toBe(consoleDocumentsFile());
  });

  it("holds every operation but persisted-operation management", () => {
    const names = consoleDocuments().map((d) => d.name);
    expect(names).toContain("Overview");
    expect(names).toContain("OnJunctionEvent");
    expect(names).toContain("RequeueDeadLetter");
    expect(names).not.toContain("PersistedOperations");
    expect(names).not.toContain("UploadPersistedOperation");
  });

  it("holds only documents a page sends", () => {
    // A document no page imports still registers on every host and keeps the operations fields it
    // selects past OperationsIsAdminOnlyTests (api/0041). Delete it with the page that stopped using it.
    const source = appSource();
    const unused = Object.keys({ ...queries, ...mutations, ...subscriptions }).filter(
      (name) => !new RegExp(`\\b${name}\\b`).test(source),
    );
    expect(unused).toEqual([]);
  });

  it("carries the __typename urql's cache adds", () => {
    const overview = consoleDocuments().find((d) => d.name === "Overview")!;
    expect(overview.text).toContain("__typename");
  });
});
