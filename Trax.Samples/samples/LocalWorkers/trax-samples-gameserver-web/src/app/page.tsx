"use client";

import { signIn, signOut, useSession } from "next-auth/react";
import { useState } from "react";

const TRAX_API = process.env.NEXT_PUBLIC_TRAX_API ?? "http://localhost:5200";

const DISCOVERY_QUERY = `{
  operations {
    trains {
      serviceTypeName
      inputTypeName
      requiredPolicies
      requiredRoles
    }
  }
}`;

const PLAYER_QUERY = `{
  discover {
    players {
      lookupPlayer(input: { playerId: "player-1" }) {
        playerId rank wins losses rating
      }
    }
  }
}`;

type GqlResult = { data?: unknown; errors?: unknown[] } | { _err: string };

export default function Home() {
  const { data: session, status } = useSession();
  const [result, setResult] = useState<GqlResult | null>(null);
  const [loading, setLoading] = useState(false);

  async function runQuery(query: string) {
    if (!session?.idToken) return;
    setLoading(true);
    setResult(null);
    try {
      const res = await fetch(`${TRAX_API}/trax/graphql`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session.idToken}`,
        },
        body: JSON.stringify({ query }),
      });
      const body = await res.json();
      setResult(body);
    } catch (err) {
      setResult({ _err: err instanceof Error ? err.message : String(err) });
    } finally {
      setLoading(false);
    }
  }

  if (status === "loading") {
    return (
      <main>
        <p className="subtitle">Loading session...</p>
      </main>
    );
  }

  if (!session) {
    return (
      <main>
        <h1>Trax GameServer · Web</h1>
        <p className="subtitle">
          Sign in with Google to obtain an id-token, then call the Trax GraphQL API with it.
        </p>
        <div className="panel">
          <h2>Not signed in</h2>
          <button onClick={() => signIn("google")}>Sign in with Google</button>
        </div>
      </main>
    );
  }

  return (
    <main>
      <h1>Trax GameServer · Web</h1>
      <p className="subtitle">
        Signed in. The Google id-token below is forwarded as{" "}
        <code>Authorization: Bearer</code> to the Trax API.
      </p>

      <div className="panel">
        <h2>Session</h2>
        <dl className="kv">
          <dt>Name</dt>
          <dd>{session.user?.name ?? "(none)"}</dd>
          <dt>Email</dt>
          <dd>{session.user?.email ?? "(none)"}</dd>
          <dt>id-token</dt>
          <dd>{session.idToken ? `${session.idToken.slice(0, 40)}...` : "(missing)"}</dd>
        </dl>
        <div className="row" style={{ marginTop: 16 }}>
          <button className="secondary" onClick={() => signOut()}>
            Sign out
          </button>
        </div>
      </div>

      <div className="panel">
        <h2>GraphQL calls</h2>
        <div className="row">
          <button onClick={() => runQuery(DISCOVERY_QUERY)} disabled={loading}>
            Discover trains
          </button>
          <button
            className="secondary"
            onClick={() => runQuery(PLAYER_QUERY)}
            disabled={loading}
          >
            Lookup player (needs Player role)
          </button>
        </div>
        {result && (
          <pre>{JSON.stringify(result, null, 2)}</pre>
        )}
      </div>
    </main>
  );
}
