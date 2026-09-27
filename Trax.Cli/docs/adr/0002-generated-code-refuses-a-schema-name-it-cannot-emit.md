---
authors: [Theauxm]
areas: [tooling]
status: accepted
---

# Generated code refuses a schema name it cannot emit, rather than rewriting it

`trax generate` turns every name in a schema (types, properties, enums and their values,
operations, groups, the project name) into a C# identifier, a namespace segment or a file
name. After the PascalCase conversion it already applies, each one must match
`[A-Za-z_][A-Za-z0-9_]*`, and a schema with any that does not is refused as a whole, every
offending name listed, before anything is written or `--force` deletes anything. Free text
(descriptions, HTTP paths) is never refused: it is collapsed to one line and escaped for where
it lands, a `///` comment, a `//` comment or a string literal.

## Status

**Accepted.**

## Considered options

**Rewrite the name** (drop the characters outside the pattern, prefix a digit with `_`), as
most OpenAPI generators do. Rejected because the name is the generated contract: the record
property, the GraphQL field HotChocolate derives from it, and the file it is written to. A
rewrite changes that contract silently and can make two schema names collide, which the
generator would then have to detect in each place it writes. A refusal costs the author one
rename in the schema, once, in a tool that is run to scaffold a project and not on every build.

**Accept any C# identifier**, Unicode letters and `@`-prefixed verbatim names included.
Rejected because C# identifiers admit formatting characters, which make generated code read
differently from what it compiles to, and the ASCII pattern is one rule every template can
rely on without knowing the C# grammar.

## Consequences

Almost everything the pattern refuses produced code that did not compile before it existed
(`2fa`, `Application/json`, a name with a space), so the refusal moves that failure from the
compiler to the generator, with the schema name in the message. The exceptions are names that
did compile: non-ASCII letters and `@`-prefixed names such as OData's `@odata.type`. A schema
that uses them has to rename them before it can be generated.

## Exemplars

- `SchemaNameValidationTests` pins the refusal: an OpenAPI property name and a tag that are not
  identifiers are refused before anything is written, `--force` deletes nothing for a refused
  schema, and a GraphQL schema whose descriptions contain code generates a library that
  compiles and declares only the schema's types.
- `SchemaTextRenderingTests` pins the escaping of free text for each place it lands.
- [CLI](/docs/reference/cli#names-and-descriptions) is the rule this produces.

Not covered: nothing checks that a new template renders schema text through
`GeneratedText`. The compile test catches it only for the fields its fixture exercises.

## Changelog

- **2026-09-27**: Recorded.
