# Trax.Samples.Auth

Securing a Trax GraphQL server end to end, in one host: API keys and JWT bearer side by side,
`[TraxAuthorize]` with roles and a policy, `[TraxAllowAnonymous]` on the public surfaces,
`GateOperations` on the operations namespace, scheme-qualified principal ids, and an audit trail
of who did what.

Full walkthrough: [traxsharp.net/docs/samples/auth](https://traxsharp.net/docs/samples/auth).

## What it proves

| Surface | Who gets in |
|---|---|
| `discover { echo }` | anyone (`[TraxAllowAnonymous]`) |
| `discover { whoAmI }` | any signed-in caller (bare `[TraxAuthorize]`) |
| `dispatch { news { publishArticle } }` | an `Editor` who passes the `VerifiedEmail` policy (two attributes, AND) |
| `discover { news { articles } }` | anyone; its `editorNote` only an `Editor` or an `Auditor` (two role attributes, OR) |
| `discover { audit { auditRecords } }` | an `Auditor` |
| `operations { ... }` | an `Operator` (`GateOperations(roles: "Operator")`) |

The same person signing in with a key and with a token gets two principal ids,
`TraxApiKey:alice` and `TraxJwt:alice`, and the audit trail records whichever one called.

The host does not call `SaveTrainParameters()`. A saved input is readable by anyone who passes the
operations gate, through `operations { executionDetail { input } }`, and `publishArticle`'s input
carries the editor note. Saving it would hand every operator a note that only editors and auditors
may read.

`tests/Trax.Samples.Auth.E2E` proves each row against the real host, over both schemes, and
proves that a Production host accepts none of the demo credentials.

## Run

```bash
docker compose up -d database          # from the Trax.Samples root; database trax_auth
dotnet run --project samples/Auth/Trax.Samples.Auth
```

`dotnet run` starts it in Development on http://localhost:5220, the only environment where the
demo credentials exist.

## Try it

```bash
# Public: no credential
curl -s localhost:5220/trax/graphql -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { echo(input: { message: \"hi\" }) { echoed } } }"}'

# The same person over two schemes
curl -s localhost:5220/trax/graphql -H 'Content-Type: application/json' \
  -H 'X-Api-Key: alice-key-do-not-use-in-production' \
  -d '{"query":"{ discover { whoAmI { id roles principalType } } }"}'

TOKEN=$(curl -s localhost:5220/dev/token/alice | sed 's/.*"token":"\([^"]*\)".*/\1/')
curl -s localhost:5220/trax/graphql -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"query":"{ discover { whoAmI { id roles principalType } } }"}'

# Erin is an Editor but her email is not verified: refused
curl -s localhost:5220/trax/graphql -H 'Content-Type: application/json' \
  -H 'X-Api-Key: erin-key-do-not-use-in-production' \
  -d '{"query":"mutation { dispatch { news { publishArticle(input: { title: \"t\", body: \"b\" }) { output { articleId } } } } }"}'

# The operations namespace needs the Operator role
curl -s localhost:5220/trax/graphql -H 'Content-Type: application/json' \
  -H 'X-Api-Key: oscar-key-do-not-use-in-production' \
  -d '{"query":"{ operations { health { status } } }"}'

# Who did what
curl -s localhost:5220/trax/graphql -H 'Content-Type: application/json' \
  -H 'X-Api-Key: oscar-key-do-not-use-in-production' \
  -d '{"query":"{ discover { audit { auditRecords(first: 5, order: { id: DESC }) { nodes { principalId success errorText } } } } }"}'
```

## Security

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for
> securing systems that use it.

The demo keys and the JWT signing key in `Auth/DemoCredentials.cs` are published in this
repository and carry `do-not-use-in-production`. `Program.cs` registers them only in
Development. In any other environment the host reads hashed API keys from `Auth:ApiKeys` and an
identity provider from `Auth:Jwt`, and with neither configured every gated surface refuses
every caller.
