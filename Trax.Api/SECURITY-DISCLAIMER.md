# Security Disclaimer

> NO WARRANTY FOR SECURITY. Trax.Api.Auth and Trax.Api.GraphQL.Audit are provided AS-IS. Trax, its authors, and contributors are NOT LIABLE for any security breach, credential leak, data loss, or damage arising from systems built on top of these packages. Securing your deployment is the SOLE RESPONSIBILITY OF THE CONSUMER.

This notice applies to every package in this repository, but particularly to `Trax.Api.Auth`, `Trax.Api.Auth.ApiKey`, and `Trax.Api.GraphQL.Audit`. These packages implement authentication and audit plumbing. They do not and cannot guarantee that a system using them is secure.

## What Trax auth IS

- A thin wrapper over ASP.NET Core's `AuthenticationHandler` and `IAuthorizationService`.
- A standardized shape for a "principal" (`TraxPrincipal`) that composes with the existing `[TraxAuthorize]` attribute.
- A bounded-channel + background-writer pipeline for persisting GraphQL request audit entries without blocking request threads.
- A set of extension points (`ITraxPrincipalResolver`, `ITraxAuditSink`, `ITraxAuditRedactor`) that consumers implement.

## What Trax auth is NOT

- A security product. Trax does not vet the cryptographic strength of keys, rotate secrets, detect compromised credentials, enforce TLS, rate-limit abusers, detect replay attacks, or perform any threat-modeling on your behalf.
- A substitute for a professional security review. Before running a system that depends on Trax auth in production, engage a security engineer to review the full stack (transport, key storage, logging, dependencies, deployment topology).
- A guarantee that sample code is safe. The demo API keys shipped in Trax samples are plaintext constants published on GitHub and NuGet. They exist only to make the samples runnable. Any system that ships them in production is broken.

## Consumer responsibility checklist

If your system uses Trax auth, you are responsible for ALL of the following. Trax does nothing about them automatically.

- Serve all traffic over HTTPS. Never accept credentials over cleartext HTTP.
- Store API keys, JWT signing secrets, and database connection strings in a secret manager (AWS Secrets Manager, HashiCorp Vault, Azure Key Vault, etc.). Never commit them to source control.
- Rotate keys on a schedule and on any suspected exposure. Implement revocation at the resolver layer.
- Rate-limit requests per principal. Trax does not do this for you. See ASP.NET Core's rate-limiting middleware.
- Redact sensitive GraphQL variables before they reach the audit sink. Implement `ITraxAuditRedactor`. Do not persist auth tokens, PII, or connection strings in plaintext audit rows.
- Monitor the `trax.audit.dropped` meter and alert when it is non-zero. A dropped audit entry is an invisible operation.
- Disable GraphQL introspection in production if you do not want schema enumeration by unauthenticated clients.
- Review your resolver for timing attacks. Dictionary lookups on a shared key space are usually fine; database lookups may leak via response-time differences.
- Use `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` when comparing keys, HMACs, or other secret tokens byte-for-byte. Plain `==` and `string.Equals` return as soon as the first differing byte is found and are timing-attack exposed.
- Validate that `[TraxAuthorize]` covers every sensitive train. Missing an attribute means the train runs for any authenticated caller.
- Log auth failures with enough context to investigate but without leaking credentials. Trax logs the fact of a failure; it does not log the key.
- Configure log sampling or rate-limiting for the `Trax.Api.Auth.ApiKey` logger category. Resolver exceptions are logged at `Warning` once per request and are not throttled by the library; a caller that can force the resolver to throw (bad input, upstream outage, etc.) will produce one log entry per request. Your logging stack is the right place to coalesce these, not the auth handler.

## Reporting vulnerabilities

Security issues are triaged on a best-effort basis. There is no SLA. File a private security advisory through the relevant repository on GitHub (`TraxSharp/*`). Do not open public issues for credential-exposure bugs.

## Final word

Using Trax auth DOES NOT hold Trax, its maintainers, or its contributors accountable for attacks against your system. MIT's `NO WARRANTY` clause is not a formality. If your deployment gets breached, compromised, or leaked, the fault and the fix are yours. Plan and staff accordingly.
