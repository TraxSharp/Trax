import { useState, useSyncExternalStore } from "react";
import { hasCredential, setCredential, onAuthChange, type AuthMode } from "../lib/auth";

// Gates the app behind a credential. Until one is set, an operator picks a mode and pastes a
// credential here; it is stored in localStorage and used for every HTTP request and subscription.
// API-key mode sends X-Api-Key / connection_init { apiKey }; Bearer mode sends
// Authorization: Bearer / connection_init { authToken }, so the dashboard can target a
// JWT-secured Trax API.
export function ConnectionGate({ children }: { children: React.ReactNode }) {
  const connected = useSyncExternalStore(onAuthChange, hasCredential);
  const [mode, setMode] = useState<AuthMode>("apikey");
  const [value, setValue] = useState("");

  if (connected) return <>{children}</>;

  const isBearer = mode === "bearer";

  return (
    <div className="flex h-screen items-center justify-center bg-sunken">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (value.trim()) setCredential(value, mode);
        }}
        className="w-full max-w-sm bg-surface rounded-xl border border-line p-8 shadow-sm"
      >
        <h1 className="text-xl font-bold text-fg">Trax Dashboard</h1>
        <p className="text-sm text-muted mt-1 mb-5">
          Connect to the Trax GraphQL endpoint.
        </p>

        <div
          role="radiogroup"
          aria-label="Auth mode"
          className="inline-flex rounded-lg border border-line-strong overflow-hidden text-sm mb-4"
        >
          {(
            [
              ["apikey", "API key"],
              ["bearer", "Bearer token"],
            ] as [AuthMode, string][]
          ).map(([m, label]) => (
            <button
              key={m}
              type="button"
              role="radio"
              aria-checked={mode === m}
              onClick={() => setMode(m)}
              className={`px-3 py-1.5 ${
                mode === m
                  ? "bg-accent text-on-accent"
                  : "bg-surface text-fg-2 hover:bg-hover"
              }`}
            >
              {label}
            </button>
          ))}
        </div>

        <label className="block text-xs font-medium text-muted mb-1">
          {isBearer ? "Bearer token (JWT)" : "API key (X-Api-Key)"}
        </label>
        <input
          autoFocus
          type="password"
          aria-label={isBearer ? "Bearer token" : "API key"}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder={isBearer ? "eyJhbGciOi…" : "admin-key-…"}
          className="w-full text-sm border border-line-strong rounded-md px-3 py-2 mb-4 focus:outline-none focus:ring-2 focus:ring-accent"
        />
        <button
          type="submit"
          disabled={!value.trim()}
          className="w-full px-4 py-2 bg-accent text-on-accent text-sm font-medium rounded-lg hover:bg-accent-hover disabled:opacity-50"
        >
          Connect
        </button>
      </form>
    </div>
  );
}
