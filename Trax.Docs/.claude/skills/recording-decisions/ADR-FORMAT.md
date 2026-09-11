# ADR format

ADRs are numbered sequentially per directory: `0001-slug.md`, `0002-slug.md`. Create the
directory lazily, only when the first ADR is needed.

A decision binding more than one repo lives in `Trax.Docs/adr/`. A decision governing one
repo lives in that repo at `<Repo>/docs/adr/`. Location is not cosmetic: it is what routes
the ADR to the people it binds, and a repo-scoped one is found by someone working in that
directory without going through the central corpus.

## Template

```md
---
authors: [<github-username>]
repos: [<which repos must obey>]
areas: [<what it is about>]
status: accepted
---

# {The decision, stated as a decision}

{1 to 3 sentences: what the context is, what was decided, and why.}

## Status

**Accepted.**

## Exemplars

- `SomeGuardTests` pins the numbering.
- [Architecture Guards](/docs/reference/architecture-guards) is the rule this produces.

Not covered: <what the guards do NOT cover>.

## Changelog

- **YYYY-MM-DD**: Recorded.
```

Three sections are required, `## Status`, `## Exemplars` and `## Changelog`, and the build
fails without them. **Everything else is optional.** An ADR can be one paragraph plus those
three. The value is in recording *that* a decision was made and *why*, and in pointing at
the things that show it in force, not in filling out sections.

## Frontmatter

Frontmatter carries the fields read by machines. Everything else, the decision and its
options and its consequences, stays prose.

| Key | Required | Shape |
| --- | --- | --- |
| `authors` | always | list of GitHub usernames, no `@` |
| `repos` | central corpus only | list of repo slugs |
| `areas` | always | list of area slugs |
| `status` | always | `proposed` / `accepted` / `deprecated` / `superseded-by-NNNN` |

Keep to these four. In particular **do not add a `date`**: git already records it, and a
single hand-written one claims to be the whole story the first time the ADR is amended. The
`## Changelog` section is where dates belong, because it says what changed as well as when.

Flow lists (`[a, b]`) and block lists (`- a` on following lines) are both accepted. A scalar
is not accepted where a list is expected: `areas: testing` is rejected, because accepting
both shapes lets the corpus drift into using each half the time.

### `authors`

The **humans whose decision this is**, not whoever typed it. When an agent writes an ADR,
the authors are the people it is working with; an agent never lists itself.

Resolve the user's username from git config:

```bash
git config --get github.user
```

**If it returns a value, use it.** That is the whole resolution, no prompt and no network
call. **If it is empty, ask**, then offer to persist it with
`git config --global github.user <login>`. Offer, do not just run it: it writes outside the
repo, so it is the user's call. Ask once per session, not once per ADR.

Do not infer it from `git log` or `git config user.email`: a commit email carries no GitHub
login.

### `repos`

Which repos must **obey** the decision. Lowercase slugs matching a workspace directory:

`core` · `effect` · `mediator` · `scheduler` · `dashboard` · `api` · `cli` · `samples` ·
`docs` · `website`

Only list a repo whose developers must obey the decision, not every one the ADR mentions in
passing. **The test is: could someone working there violate this without realising?** If
yes it belongs in the list; if they would merely find it interesting, it does not.

Be explicit rather than economical. A decision about the train naming rule binds the
dashboard and the scheduler even though it is implemented in the mediator, because they are
the ones who can break it by comparing the wrong name.

A **repo-scoped** ADR omits the key entirely: it lives in `<Repo>/docs/adr/`, and its path
already says which repo governs it. Restating it is a second place to drift, and the guard
rejects it.

The list matters because it is what reaches the person about to break the decision.
Under-list it and they never see it; over-list it and it stops meaning anything.

### `areas`

What the ADR is *about*, the subject someone would go looking under. Where `repos` answers
"who must obey this", `areas` answers "where would I find this".

The vocabulary is passed to the guard with `--known-areas` and is closed on purpose. Adding
a new one is a deliberate edit to the CI job, the same way a new repo slug would be,
because a vocabulary that grows freely stops discriminating. Pick every one that genuinely
applies, usually one to three.

The two axes exist because one cannot do both jobs. A foundational decision legitimately
binds every repo, so `repos` on those tells a reader nothing about what they are reading.
That is `repos` working correctly, not failing, and `areas` is what carries the subject.

## The `## Status` section

The frontmatter `status` is what tooling reads; the section is what a human reads first,
and it is the only place a supersession has room to be explained. The guard checks the two
agree, so they cannot drift apart.

Open it with the bolded status and nothing in front:

```md
## Status

**Accepted.** Supersedes [0003](./0003-the-decision-this-replaced.md), which ran the
dependency the other way and made every new repo pay for it.
```

The four openings are `**Proposed.**`, `**Accepted.**`, `**Deprecated.**`, and
`**Superseded by**` followed by a link to the ADR that replaced it.

**A supersession is two edits or it is a lie.** The superseding ADR writes `Supersedes` and
links the ADR it replaced; the superseded one sets `status: superseded-by-NNNN` and writes
`**Superseded by**` with a link back. The guard fails the half-done one, because a reader
arriving from a code comment lands on the *old* document, where a forward link they cannot
see does them no good.

## The `## Exemplars` section

The things that show the decision in force. This section is what lets an ADR stay short:
anything it would otherwise have to explain goes here as a link instead.

It must be in **one of three states**.

**1. Guard classes in this repo.** A bare backticked class name **ending in `Tests`** is an
enforcement claim. A name carrying a dot (`Trax.Core.HygieneGuardsTests`) is prose, as is a
file cited with its extension, and so is anything inside a fenced code block. The guard
checks a claimed class exists under the repo's test roots, and that it **cites the ADR
back**:

```md
## Exemplars

- `MigrationsIntegrityTests` pins sequential numbering and the embedded-resource glob.
- [Data persistence](/docs/effect/effect-providers/data-persistence) is the rule this produces.

Not covered: the guard checks numbering, not that the DDL matches the EF model.
```

The back-citation goes in the class docstring **and** in the assertion failure message.
`NoSilentRegistrationOrderDependenceTests` in Trax.Api is the model. The failure message is
the more valuable half, because it puts the authority in front of the person who just
tripped the guard.

Resolution only proves the class still **exists**, which a rename breaks loudly and a
rewrite does not. Someone gutting a guard's assertions sees nothing telling them an ADR
depends on it, and the ADR goes on claiming enforcement that has quietly stopped. The
back-citation puts that warning where the person editing the test is looking.

**2. `**Enforced elsewhere:**`.** The guards are real but live in another repo. This is the
normal state for the central corpus: a decision binding eight repos is held up by guards in
those repos, and no checkout of `Trax.Docs` can see them.

```md
**Enforced elsewhere:** `NoIgnoreAttributeTests` in each repo's Tests.Meta project, and
HygieneGuards.NoIgnoreAttribute shipped from Trax.Core.Testing.
```

Recorded, not verified. Say what and where, specifically enough that a reader can go and
look.

**3. `**Unenforced:**`.** Nothing checks it, and here is why nothing can.

```md
**Unenforced:** a framework choice is not a rule code can break. Nothing here compiles
without it, so there is no state of the repo where this is violated.
```

That is a valid and useful answer. What is not allowed is **silence**: an ADR with no
enforcement and no admission reads as a rule the build is holding for you. Claiming
enforcement alongside `**Unenforced:**` is a contradiction and fails. States 1 and 2 may be
combined, when some of the enforcement is local and some is not.

The same bar applies to the `**Enforced elsewhere:**` text as to `**Unenforced:**`: it must
be specific, and a deferral is rejected.

The reason has to be a reason. Under 40 characters fails, and so does anything reading as a
deferral ("not yet", "TODO", "later", "a follow-up"). A rule that should be recorded and is
not yet should be **recorded**, not exempted.

**Say what is not covered.** That half is prose no test can verify, and it is the half that
stops a reader over-trusting the link. Naming `MigrationsIntegrityTests` against the
migrations ADR is true, but it guards numbering, not the decision itself.

One trap: a test *file* you mean as an example to copy rather than as enforcement must be
cited with its extension (`FooTests.cs`), not as a bare backticked class name, or the guard
reads it as an enforcement claim.

## The `## Changelog` section

Newest first, one line each:

```md
## Changelog

- **2026-09-11**: Narrowed to entity namespaces; the operation surface moved to 0018.
- **2026-08-25**: Recorded.
```

Git has every edit and cannot tell an amendment from a typo fix. This is the curated half:
**substantive changes only**. The decision changed, its scope changed, a claim in it turned
out to be wrong, or material moved out to another document. A reformat is worth an entry
only when it moved something; "fixed a link" is not.

A new ADR gets one entry: `- **YYYY-MM-DD**: Recorded.`

## Optional sections

Only include these when they add something. Most ADRs will not need them.

- **Considered options**, only when the rejected alternatives are worth remembering
- **Consequences**, only when non-obvious downstream effects need calling out
- **Why this is written down**, when the reason to record it is not the decision itself

Each earns its place by saying something a reader cannot get from the code. A "consequence"
that restates the rule is the documentation page's job.

## Coming from the other side: you wrote a guard

Where the census is switched on, the question runs the other way too. Every guard class
under the census root must be **named by an ADR**, or opt out in its own docstring:

```csharp
/// <summary>
/// Migration files are numbered sequentially.
///
/// <para>Not ADR-enforcing: it pins a file naming convention nobody weighed an
/// alternative for, and no reader would ask why it is like this.</para>
/// </summary>
```

A new guard is **unclassified until you choose**, and the build says so. Opting out is a
normal answer. The reason is held to the same bar as `**Unenforced:**`: specific, and not a
deferral. Claiming both an ADR and the opt-out fails, because they contradict each other.

The marker is read from the docstring directly above the class, so two guards in one file
are asked separately and a marker inside a string literal is not an answer.

## Numbering and the index

Scan the ADR's **own directory** for the highest existing number and increment by one.
Numbering is per directory, so a bare number is ambiguous across them. Cite a repo-scoped
ADR as `effect/0001`, or by path.

**Then add it to that directory's `README.md`**, every table: by area, the full list, and
by repo in the central corpus. The guard checks each against the frontmatter in both
directions, including the full table's tag columns, and fails until they agree, so a new
ADR is not finished when the file is written.

Index links must be written `[0007](./0007-slug.md)`, with the `./`. Without it the link is
not recognised and the row reads as empty.

## Voice

Match the rest of the Trax documentation.

- **No em-dashes.** Use commas, periods, or parentheses. Both `NoEmDashesTests` and the
  guard's hygiene check reject them.
- **No filler.** No "ensure", "leverage", "enhance", "it is worth noting". Say what the
  thing does.
- **Write the title as the decision**, not as a topic: "Schema changes are hand-written
  SQL", not "Migrations".
