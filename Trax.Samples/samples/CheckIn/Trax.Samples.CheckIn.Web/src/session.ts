import { configureTrax, TraxOperationError } from "./trax/index.g";

// The host: localhost:5282 in development, or what the build was given (the browser tests point it at their host).
export const API_ORIGIN: string = import.meta.env.VITE_CHECKIN_API ?? "http://localhost:5282";

export type Role = "passenger" | "agent";

export interface Person {
  name: string;
  role: Role;
  token: string;
}

const KEY = "trax.checkin.person";

/**
 * Signs one person in: the host's Development-only endpoint answers a token for that name and role alone. The page
 * holds no other person's credential; a second person signs in on a page of their own.
 */
export async function signIn(name: string, role: Role): Promise<Person> {
  const response = await fetch(`${API_ORIGIN}/dev/token`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name, role }),
  });
  const body = await readBody(response);
  if (!response.ok || !body.token) throw new Error(body.error ?? `Sign-in failed (${response.status}).`);
  const person = { name, role, token: body.token };
  sessionStorage.setItem(KEY, JSON.stringify(person));
  return person;
}

/** The answer's JSON, or nothing when it has none: an unmapped endpoint answers 404 with no body. */
async function readBody(response: Response): Promise<{ token?: string; error?: string }> {
  if (!response.headers.get("Content-Type")?.includes("application/json")) return {};
  try {
    return (await response.json()) as { token?: string; error?: string };
  } catch {
    return {};
  }
}

/** The person stored by the last sign-in, or nothing once their token has expired: the host would refuse it. */
export function signedIn(): Person | null {
  const stored = sessionStorage.getItem(KEY);
  if (!stored) return null;
  const person = JSON.parse(stored) as Person;
  const expires = expiresAt(person.token);
  if (expires === null || expires <= Date.now()) {
    signOut();
    return null;
  }
  return person;
}

/** When the token stops being good, in milliseconds since the epoch, read from its `exp`; null when it has none. */
function expiresAt(token: string): number | null {
  try {
    const payload = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const { exp } = JSON.parse(atob(payload)) as { exp?: unknown };
    return typeof exp === "number" ? exp * 1000 : null;
  } catch {
    return null;
  }
}

const signedOutListeners = new Set<() => void>();

export function signOut(): void {
  sessionStorage.removeItem(KEY);
}

/** Calls `listener` when a refusal signs the person out, until the function it returns is called. */
export function whenSignedOut(listener: () => void): () => void {
  signedOutListeners.add(listener);
  return () => {
    signedOutListeners.delete(listener);
  };
}

/**
 * Signs the person out when the host refused a read because it does not know them: their token expired, or the host
 * no longer takes it. True when it did, so the caller shows the sign-in form rather than the refusal.
 */
export function signOutOnRefusal(error: unknown): boolean {
  if (!(error instanceof TraxOperationError) || error.code !== "UNAUTHENTICATED") return false;
  signOut();
  for (const listener of [...signedOutListeners]) listener();
  return true;
}

/**
 * Points the generated client at the host as this person: the token on each request and on the socket. The client
 * refuses cleartext to any host but loopback, so a build pointed at another machine needs https.
 */
export function connect(person: Person): void {
  configureTrax({
    url: `${API_ORIGIN}/trax/graphql`,
    socketUrl: `${API_ORIGIN.replace("http", "ws")}/trax/graphql`,
    headers: () => ({ Authorization: `Bearer ${person.token}` }),
    connectionParams: () => ({ authToken: person.token }),
    user: `${person.role}:${person.name}`,
  });
}
