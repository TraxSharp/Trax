# Trax GameServer · Web

A minimal Next.js companion for `Trax.Samples.GameServer.Api`. Signs users in
with Google via NextAuth, then forwards the Google-issued id-token to the
Trax GraphQL API as an `Authorization: Bearer` credential. The API validates
the token against Google's JWKS via `AddTraxJwtAuth("https://accounts.google.com", googleClientId)`.

> **Most apps don't need a custom resolver.** The actual integration is one
> line — `services.AddTraxJwtAuth("https://accounts.google.com", clientId)` —
> and Trax's default resolver handles standard OIDC claims. This sample only
> has a `GoogleJwtResolver` class because it hard-assigns the `Player` role
> to every signed-in user so the trains are exercisable; see the comments on
> that class for "when do I actually need a custom resolver?"

## Prerequisites

- Node.js 20 or newer, npm 10 or newer
- A running `Trax.Samples.GameServer.Api` on `http://localhost:5200`
- A Google Cloud project with an OAuth 2.0 client

## 1. Create a Google OAuth client

1. Open <https://console.cloud.google.com/apis/credentials>.
2. Create a new OAuth 2.0 Client ID of type **Web application**.
3. Add authorized redirect URI:
   ```
   http://localhost:3000/api/auth/callback/google
   ```
4. Save the client id and client secret.

## 2. Configure the web app

```bash
cp .env.local.example .env.local
```

Fill in:

- `AUTH_SECRET` — generate with `node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))"`
- `AUTH_GOOGLE_ID` and `AUTH_GOOGLE_SECRET` — from step 1
- `NEXT_PUBLIC_TRAX_API` — leave as `http://localhost:5200` unless the API runs elsewhere

## 3. Configure the Trax API

Open `../Trax.Samples.GameServer.Api/appsettings.json` and set:

```json
"Google": {
  "ClientId": "<the same AUTH_GOOGLE_ID from step 1>"
}
```

The API uses this value as the expected `aud` claim when validating tokens.
If it is blank, the API silently skips JWT registration and only accepts
API-key credentials.

## 4. Run

```bash
# Terminal 1: API (from the repo root)
dotnet run --project Trax.Samples/samples/LocalWorkers/Trax.Samples.GameServer.Scheduler
dotnet run --project Trax.Samples/samples/LocalWorkers/Trax.Samples.GameServer.Api

# Terminal 2: web
cd Trax.Samples/samples/LocalWorkers/trax-samples-gameserver-web
npm install
npm run dev
```

Open <http://localhost:3000>, sign in, click **Discover trains**.

## How the pieces fit

```
┌────────────┐    1. sign in        ┌──────────┐
│  Browser   │ ────────────────────>│  Google  │
│ (Next.js)  │ <── id-token ────────│   OIDC   │
└────────────┘                      └──────────┘
      │
      │ 2. fetch /trax/graphql
      │    Authorization: Bearer <id-token>
      ▼
┌────────────────────────┐
│ Trax GameServer · API  │
│                        │
│ AddTraxJwtAuth(        │
│   "accounts.google.com"│──── 3. fetch JWKS ────> Google
│   clientId             │     (cached)
│ )                      │
│                        │
│ GoogleJwtResolver maps │
│ sub → TraxPrincipal.Id │
└────────────────────────┘
```

NextAuth owns the interactive browser flow. The Trax API is pure JWT
validation: no cookies, no redirects, no session state.
