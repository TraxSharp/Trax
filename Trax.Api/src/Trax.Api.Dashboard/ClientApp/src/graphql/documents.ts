import { formatDocument, stringifyDocument } from "@urql/core";
import { Kind, type DocumentNode, type OperationDefinitionNode } from "graphql";
import * as mutations from "./mutations";
import * as queries from "./queries";
import * as subscriptions from "./subscriptions";

// The documents the console sends, as the host registers them when it runs persisted operations.
//
// Each is the text urql puts on the wire: the document as written, with the __typename its cache
// adds to every selection (formatDocument), printed the way urql prints it. The host stores that
// text under an id carrying its hash and tells the page the id in its runtime config, so with
// persisted operations required the console sends ids and never text. A document that reaches
// persistedOperations is left out: the host runs those as text, behind their own management gate.

/** One console document: its operation's name and the text urql sends for it. */
export interface ConsoleDocument {
  name: string;
  text: string;
}

const MANAGEMENT_ROOT = "persistedOperations";

function operationOf(document: DocumentNode): OperationDefinitionNode {
  const operations = document.definitions.filter(
    (d): d is OperationDefinitionNode => d.kind === Kind.OPERATION_DEFINITION,
  );
  if (operations.length !== 1 || !operations[0].name)
    throw new Error("A console document holds exactly one named operation.");
  return operations[0];
}

const isDocument = (value: unknown): value is DocumentNode =>
  typeof value === "object" && value !== null && (value as DocumentNode).kind === Kind.DOCUMENT;

/** Every document the console sends that the host registers, in name order. */
export function consoleDocuments(): ConsoleDocument[] {
  const documents = new Map<string, ConsoleDocument>();
  for (const value of [
    ...Object.values(queries),
    ...Object.values(mutations),
    ...Object.values(subscriptions),
  ]) {
    if (!isDocument(value)) continue;
    const operation = operationOf(value);
    const roots = operation.selectionSet.selections.map((s) =>
      s.kind === Kind.FIELD ? s.name.value : "",
    );
    if (roots.includes(MANAGEMENT_ROOT)) continue;
    const name = operation.name!.value;
    if (documents.has(name)) throw new Error(`Two console documents are named '${name}'.`);
    documents.set(name, { name, text: stringifyDocument(formatDocument(value)) });
  }
  return [...documents.values()].sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
}

/** The file Trax.Api.Dashboard embeds, as scripts/export-documents.ts writes it. */
export function consoleDocumentsFile(): string {
  return `${JSON.stringify({ format: 1, documents: consoleDocuments() }, null, 2)}\n`;
}
