import { configureTrax } from "./trax/index.g";

// The host: localhost:5280 in development, or what the build was given (the browser tests point it at their host).
export const API_ORIGIN: string = import.meta.env.VITE_BAKERY_API ?? "http://localhost:5280";

export type Role = "customer" | "baker";

export interface Person {
  name: string;
  role: Role;
  token: string;
}

const KEY = "trax.bakery.person";

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

export function signedIn(): Person | null {
  const stored = sessionStorage.getItem(KEY);
  return stored ? (JSON.parse(stored) as Person) : null;
}

export function signOut(): void {
  sessionStorage.removeItem(KEY);
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
