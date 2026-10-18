// Writes console-documents.json, the documents Trax.Api.Dashboard embeds and a host running
// persisted operations registers. Run it (npm run documents) after changing any document under
// src/graphql; src/graphql/documents.test.ts fails while the file is out of date.
import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { consoleDocumentsFile } from "../src/graphql/documents";

const path = fileURLToPath(new URL("../console-documents.json", import.meta.url));
writeFileSync(path, consoleDocumentsFile());
console.log(`Wrote ${path}`);
