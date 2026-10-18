import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { consoleDocuments, consoleDocumentsFile } from "./documents";

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

  it("carries the __typename urql's cache adds", () => {
    const overview = consoleDocuments().find((d) => d.name === "Overview")!;
    expect(overview.text).toContain("__typename");
  });
});
