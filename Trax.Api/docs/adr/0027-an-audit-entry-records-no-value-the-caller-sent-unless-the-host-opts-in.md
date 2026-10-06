---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# An audit entry records no value the caller sent unless the host opts in

The audit pipeline writes every GraphQL request to a sink the host owns, and an audit store is
kept long and read by more people than the API is. A value a caller sends, a password, a token, an
email address, is exactly what OWASP's logging guidance says not to write there. So the document is
recorded with every string and numeric literal replaced by a placeholder, variables are not
recorded at all unless the host registers an `ITraxAuditRedactor` that returns them, and error
messages are not recorded unless the host sets `RecordErrorMessages`. That redactor receives the
variables as a JSON object it can walk, so it can remove a field at any depth.

## Status

**Accepted.**

## Considered options

**Record the document as sent and redact variables by name (what shipped before).** A redactor saw
a flat dictionary of top-level variables, each already flattened to a string, so it could not reach
`$input.password`, and it never saw the document, so `login(password: "...")` written inline was
recorded verbatim whatever it did. The default redactor passed everything through. The guidance to
"redact secrets" could not be followed.

**Keep the document as sent and document the limit.** Puts the burden on every client author to
never inline a value, which a reviewer of the server cannot check.

**Apollo's usage-reporting signature** (`stripSensitiveLiterals` with list and object literals
emptied, aliases removed, selections sorted). It is the established way to strip values from a
GraphQL document, and the transform here is the same for strings and numbers. Emptying input
objects and removing aliases suits grouping operations for metrics; an audit record should still
show which fields a call set and the shape the caller sent, so input-object field names, list
lengths, aliases, booleans, enum values and `null` are kept. Apollo's operation registry signature
makes the same choice.

**A redactor that receives the document.** Every host would reimplement literal stripping. Trax
does it once, with HotChocolate's own `SyntaxRewriter`, before any host code runs.

**Omit variables by default.** Apollo's `sendVariableValues` defaults to `{ none: true }` for the
same reason: a host that wants them says so, and chooses which.

**Record error messages as written (what shipped first).** A message is written by HotChocolate or
a resolver, and either can quote the input: a resolver that throws `$"Password {password} was
rejected."` put the password in the audit store beside a document with every literal removed.
Apollo's usage reporting masks error messages by default (`sendErrors: { masked: true }`) for this
reason. An entry records each error's code and path instead, which is schema and server
vocabulary, and a request-level exception as its type.

## Consequences

**A sink sees `""` and `0` where the caller sent values.** A record shows what was called and which
inputs were set, not with what. Booleans and enum values are schema vocabulary and are kept.

**Variables are `null` in every entry until the host registers a redactor.** `TraxAuditEntry.Variables`
and `ITraxAuditRedactor.Redact` are `JsonObject`, which a sink can write to a JSON column as it is.

**A redactor that throws records no variables**, and the entry is still written.

**`ErrorText` reads `CODE at path`, one per error, joined with `; `.** An error with no code
reads `<masked>`; an exception the pipeline raised reads as its type name, or as the codes of the
errors a `GraphQLException` carries. A host whose sink may hold what callers send sets
`RecordErrorMessages` and gets the messages.

## Exemplars

- `TraxGraphQLAuditListenerTests` pins it: an inline password and every string and number in a
  document, including nested input objects, list items, variable defaults and the arguments of a
  directive at each of its locations, are absent from the entry while the field names remain; the
  default redactor records no variables; a redactor removes `input.password` from a nested input
  object; a resolver error and a variable coercion error that quote the input leave it out of
  `ErrorText` unless the host sets `RecordErrorMessages`.

Not covered: an error's code and path are recorded as the resolver set them. A resolver that puts
an input value in its error code, or a host that sets `RecordErrorMessages`, puts it in the audit.

## Changelog

- **2026-10-01**: Error messages are no longer recorded by default; `ErrorText` holds each error's
  code and path, and `RecordErrorMessages` opts in to the messages. The literal stripper now
  reaches directive arguments, which the ADR already claimed and HotChocolate's rewriter skips.

- **2026-09-30**: Recorded.
