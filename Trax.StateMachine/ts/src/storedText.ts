// What a stored snapshot may not hold although JSON.parse reads it. The C# engine is the source of truth for a
// rehydrate's refusal code, and its reader (System.Text.Json) refuses more than JSON.parse does: a document nested
// deeper than 64 containers, a key written twice in one object, and a string or key holding a lone surrogate, which
// it cannot decode. It only reads the members it uses, so a duplicate or a lone surrogate inside a member it ignores
// goes unnoticed there, and this reports it per member to match. Then, once the state is known, a value no store can
// hold (a NUL character, a number outside the range of a double) is refused as MALFORMED too (C# `StorableJson`).

/** The deepest nesting of objects and arrays the C# reader accepts, the root counting as one. */
export const MAX_STORED_DEPTH = 64;

export interface StoredTextProblems {
  /** Deeper than {@link MAX_STORED_DEPTH}, a top-level key written twice or holding a lone surrogate, or raw text
   * that is not well-formed UTF-16. */
  readonly root: boolean;
  /** The top-level members whose value holds a key written twice or a string with a lone surrogate. */
  readonly members: ReadonlySet<string>;
}

const LONE_SURROGATE =
  /[\ud800-\udbff](?![\udc00-\udfff])|(?<![\ud800-\udbff])[\udc00-\udfff]/;

const hasLoneSurrogate = (s: string): boolean => LONE_SURROGATE.test(s);

const isSpace = (c: string | undefined): boolean =>
  c === " " || c === "\t" || c === "\n" || c === "\r";

const ESCAPES: Record<string, string> = {
  '"': '"',
  "\\": "\\",
  "/": "/",
  b: "\b",
  f: "\f",
  n: "\n",
  r: "\r",
  t: "\t",
};

// The string starting at the quote at `start`, decoded, and the index just past its closing quote.
function readString(text: string, start: number): [string, number] {
  let out = "";
  let i = start + 1;
  while (i < text.length && text[i] !== '"') {
    if (text[i] === "\\") {
      const e = text[i + 1];
      if (e === "u") {
        out += String.fromCharCode(parseInt(text.slice(i + 2, i + 6), 16));
        i += 6;
      } else {
        out += ESCAPES[e] ?? e;
        i += 2;
      }
    } else out += text[i++];
  }
  return [out, i + 1];
}

/**
 * Scans text JSON.parse has already accepted for what the C# reader refuses. Iterative, so a deep document cannot
 * overflow the stack.
 */
export function storedTextProblems(text: string): StoredTextProblems {
  const members = new Set<string>();
  if (hasLoneSurrogate(text)) return { root: true, members };
  // Each open container: the keys seen so far in an object, null for an array.
  const open: (Set<string> | null)[] = [];
  let member: string | undefined;
  let root = false;
  let i = 0;
  while (i < text.length) {
    const c = text[i];
    if (c === "{" || c === "[") {
      open.push(c === "{" ? new Set() : null);
      if (open.length > MAX_STORED_DEPTH) return { root: true, members };
      i++;
    } else if (c === "}" || c === "]") {
      open.pop();
      i++;
    } else if (c === '"') {
      const [value, end] = readString(text, i);
      i = end;
      let next = i;
      while (isSpace(text[next])) next++;
      const keys = open[open.length - 1];
      const isKey = keys != null && text[next] === ":";
      const bad = hasLoneSurrogate(value) || (isKey && keys.has(value));
      if (isKey) keys.add(value);
      if (isKey && open.length === 1) {
        member = value;
        if (bad) root = true;
      } else if (bad && member !== undefined) members.add(member);
    } else i++;
  }
  return { root, members };
}

/**
 * A message naming the first value in `node` no store can hold (a NUL character in a string or a key, a number that
 * is not finite), or null when every value can be stored.
 */
export function unstorable(node: unknown): string | null {
  if (typeof node === "string")
    return node.includes("\0")
      ? "A string holds a NUL character, which cannot be stored."
      : null;
  if (typeof node === "number")
    return Number.isFinite(node)
      ? null
      : "A number is outside the range of a double.";
  if (Array.isArray(node)) {
    for (const item of node) {
      const problem = unstorable(item);
      if (problem != null) return problem;
    }
    return null;
  }
  if (node !== null && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      if (key.includes("\0"))
        return "A key holds a NUL character, which cannot be stored.";
      const problem = unstorable(value);
      if (problem != null) return problem;
    }
  }
  return null;
}
