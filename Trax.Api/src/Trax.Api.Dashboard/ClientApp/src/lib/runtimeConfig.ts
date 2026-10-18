// Where the app is served and where its GraphQL endpoint is, read when the page loads.
//
// One build runs under any path: the Trax.Api.Dashboard package writes this into index.html as it
// serves it, as <meta name="trax-dashboard-config" content='{"basePath":…,"graphqlPath":…}'>,
// beside a <base href> at the same base path that the build's relative asset URLs resolve against.
// Without the tag (vite dev, the tests) the app is at the root and talks to /trax/graphql, which
// the dev server proxies to the API host.
//
// Both values must be paths on this origin. Anything else is ignored rather than followed, so a
// page can never be pointed at another host's API with the credential an operator entered.
//
// A host that runs persisted operations also names the id it registered each console document
// under (src/graphql/documents.ts), and the console then sends those ids instead of text. Only an
// id under the console's prefix is kept.

export interface RuntimeConfig {
  /** The path the app is served under, with a trailing slash. */
  basePath: string;
  /** The path of the Trax GraphQL endpoint, on this origin. */
  graphqlPath: string;
  /** The persisted id of each console document, by operation name; empty when the host sends none. */
  documents: Readonly<Record<string, string>>;
}

const DEFAULTS: RuntimeConfig = { basePath: "/", graphqlPath: "/trax/graphql", documents: {} };

/** The prefix every console document's id is under. */
export const CONSOLE_DOCUMENT_PREFIX = "trax_console_";

const OPERATION_NAME = /^[_A-Za-z][_0-9A-Za-z]*$/;

const isLocalPath = (value: unknown): value is string =>
  typeof value === "string" && value.startsWith("/") && !value.startsWith("//") && !value.includes("\\");

export function readRuntimeConfig(doc: Document = document): RuntimeConfig {
  const content = doc
    .querySelector<HTMLMetaElement>('meta[name="trax-dashboard-config"]')
    ?.getAttribute("content");
  if (!content) return { ...DEFAULTS };

  let parsed: unknown;
  try {
    parsed = JSON.parse(content);
  } catch {
    return { ...DEFAULTS };
  }
  const raw = (parsed ?? {}) as Partial<Record<keyof RuntimeConfig, unknown>>;
  return {
    basePath: isLocalPath(raw.basePath) ? withTrailingSlash(raw.basePath) : DEFAULTS.basePath,
    graphqlPath: isLocalPath(raw.graphqlPath) ? raw.graphqlPath : DEFAULTS.graphqlPath,
    documents: documentIds(raw.documents),
  };
}

function documentIds(value: unknown): Record<string, string> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return {};
  const ids: Record<string, string> = {};
  for (const [name, id] of Object.entries(value))
    if (OPERATION_NAME.test(name) && typeof id === "string" && id.startsWith(CONSOLE_DOCUMENT_PREFIX))
      ids[name] = id;
  return ids;
}

/** The router's basename: the base path without its trailing slash ("" at the root). */
export const routerBasename = (config: RuntimeConfig): string => config.basePath.replace(/\/$/, "");

/** The endpoint's HTTP URL, and its subscription socket on this page's host. */
export function graphqlUrls(
  graphqlPath: string,
  location: Pick<Location, "protocol" | "host"> = window.location,
): { http: string; ws: string } {
  const scheme = location.protocol === "https:" ? "wss" : "ws";
  return { http: graphqlPath, ws: `${scheme}://${location.host}${graphqlPath}` };
}

function withTrailingSlash(path: string): string {
  return path.endsWith("/") ? path : `${path}/`;
}
