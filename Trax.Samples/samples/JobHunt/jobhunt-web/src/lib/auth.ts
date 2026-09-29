const USERS = [
  { key: "alice-key-do-not-use-in-production", id: "alice", name: "Alice" },
  { key: "bob-key-do-not-use-in-production", id: "bob", name: "Bob" },
  { key: "charlie-key-do-not-use-in-production", id: "charlie", name: "Charlie" },
] as const;

export type User = (typeof USERS)[number];

export function getApiKey(): string {
  return localStorage.getItem("jobhunt-api-key") || "alice-key-do-not-use-in-production";
}

export function setApiKey(key: string) {
  localStorage.setItem("jobhunt-api-key", key);
  window.location.reload();
}

export function getCurrentUser(): User {
  const key = getApiKey();
  return USERS.find((u) => u.key === key) || USERS[0];
}

export function getAllUsers() {
  return USERS;
}
