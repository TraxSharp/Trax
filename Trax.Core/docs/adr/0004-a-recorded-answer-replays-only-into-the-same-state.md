---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# A recorded answer replays only into the same state

A run that repeats an earlier one (a requeue, and an automatic retry) replays the earlier run's
answers instead of asking its deciders again (`Trax.Docs/adr/0041`). Trax.Core replays an answer
only when the state the question is asked about now hashes exactly as the state the answer was
given about did. Otherwise, including when either hash is missing, the decider is asked afresh.
The hash is keyed when the host supplies a key, and a host should supply one.

## Status

**Accepted.** Adds a state check to the matching rule of `Trax.Docs/adr/0041`: a recorded
answer replays only into the same state.

## Why this is written down

Because a repeated run runs every
step again and reads the state a question is about afresh, and an answer is a decision about one
state. The property this rule keeps is that a recorded answer replays only into the same state,
and a repeated run, which is usually unattended, otherwise asks.

## Considered options

**Match on question, occurrence and fingerprint only.** Rejected: that says an answer was given
to the same asking, not about the same state.

**Each replay implementation compares the state.** Rejected: every journal would have to get it
right, and one that forgot would replay silently. The hash is computed and compared in
Trax.Core, so a journal only stores `DecisionMade.StateHash` and returns it as
`RecordedAnswer.StateHash`, and every replay inherits the check.

**Store the state, not its hash.** Rejected: the journal would hold a second copy of the run's
data, and comparing needs equality on arbitrary types.

**Hash the JSON the state is written as.** Rejected: JSON does not cover the whole state an
in-process decider reads (a tuple's items, public fields, `[JsonIgnore]` and non-public members,
the members of a derived type held as its base type or an interface). The hash walks every
instance field of each value's runtime type, public or not, base type first and by name, writing
each value's runtime type (named with its assembly) and an unambiguous encoding of its
primitives. Every element of an array, inline array or fixed buffer counts, and so does each
array dimension's length and lower bound. An enum's type is written with its underlying type and
each member's name and value, and a local `DateTime` with every bit of it, the daylight-saving
flag included. A framework collection is written as its elements and its comparers (`Comparer`,
`KeyComparer`, `ValueComparer`); a wrapper (a read-only wrapper, a `Collection<T>` and so a keyed
or observable collection, a view of a dictionary's keys) as what it wraps, with its own comparers,
so a key view is written as its whole dictionary and a list of the state's own type by its fields.
A set, dictionary or bag is written with its elements sorted by their encoding, so equal contents
hash alike whatever order they were added or enumerated in, in any process.

**An unkeyed hash.** Rejected as the default a host should run with: the hash covers every value
in the state, including members a host masks or withholds elsewhere, and the journal stores it
beside the answer. Under a `StateHashKey` it is an HMAC-SHA256 that only the key's holder can
compute. Without one it stays a SHA-256, so a host with no key still replays, and the two are told
apart by their prefix (`k1:`, `s1:`) and never match each other.

**Replay an answer recorded without a hash.** Rejected: an answer recorded before this rule
cannot be shown to be about the same state, and the safe reading of "cannot tell" is "ask".

## Consequences

A state whose fields differ from run to run although nothing that matters changed (a timestamp,
a cache, a generated id, a private counter) is asked afresh
on every repeat, so it never replays. That is the cost of failing closed, and it falls on the
deciders, not on the run's correctness.

A state the hash cannot read the same way every time has no hash and never replays, and that is
never the run's failure: one holding a reference cycle, a delegate (an ORM's lazy-loading proxy,
say), a pointer or native handle, a type, member or assembly, a stream, wait handle, task or
thread, a culture other than the framework's shared instance of it, a non-generic `Hashtable` or
`SortedList`, a `BlockingCollection<T>`, a wrapper `ArrayList` makes, a collection that enumerates
by its own code, a field whose read throws, a collection whose enumeration throws, or one nested
deeper than 64 levels or larger than the hash's size caps. Each of those collections holds
something beyond its elements and comparers that cannot be read without running code that may be
the state's own, and the hash never runs it.

Only the hash leaves the run, never the state. A host should register a `StateHashKey` in its
container, shared by every process that may repeat a run: the hash covers values the host masks
elsewhere, and keyed it cannot be computed without the key. Adding or changing the key means each
answer recorded before it is asked afresh once.

The hash covers the state's value and nothing else. What a decider reads from elsewhere (a
customer it looks up by the id the state holds, say) is not in it, so a change there is noticed
only when the state carries the value itself.

The encoding has caps: 64 levels of nesting, 1,000,000 values and 16 MiB of encoding. A state
past one has no hash and never replays. Each type is written in full once per hash and by index
after that, so a list of a million small values fits within the byte cap.

A refused replay's reason says what does not fit and never quotes the recorded answer: Trax.Core
cannot know which questions are sensitive, and the reason is logged and recorded.

A refused replay is reported, not hidden: `DecisionMade.ReplayRefused` says the answer was given
about a different state (or under a key that has changed since), was recorded without a hash, was
recorded under a key when this run hashes without one or the other way round, or could not be
compared because the state could not be hashed or the key could not be resolved.

## Exemplars

- `StateDigestTests` pins that states differing only where JSON would not look (a tuple, a public
  field, a derived type held as its base or an interface, a `[JsonIgnore]` property, a private
  field) hash differently, that equal states built separately hash alike, and that a cycle, a
  delegate, a type or a stream, and nesting past the limit give no hash. It pins the encoding
  with golden `s1:` and `k1:` vectors, the value and byte caps, a shared graph, inline arrays and
  fixed buffers, comparers and wrappers (key views, `Collection<T>`, keyed collections), a `Uri`
  subclass, array lower bounds, each leaf encoding, an enum's members, a local time's
  daylight-saving flag, the fields a type derived from a collection adds, order-independence of
  unordered collections, that keyed and unkeyed hashes never match, and that a culture other
  than the shared instance and each refused collection give no hash without running the
  state's code.
- `DecisionRuntimeTests` pins the same shapes through a replay; that the same state replays for
  `Decide`, `Switch`, `Gate` and `Scale`; that a different state at any occurrence of a
  question, an answer recorded without a hash and a state that cannot be hashed are all
  asked afresh with the reason in `ReplayRefused`, which names the key when the key is what
  differs; that a key that cannot be resolved replays nothing; that `StateHash` is a hash, never
  the state;
  that `QuestionType` is set on decisions and refusals; that a key from the container keys the
  hash; and that a refusal's reason does not quote the recorded answer.
- [Decisions](/docs/core/decisions) is the rule this produces.

Not covered: nothing checks that a
host's journal stores and returns the hash. One that drops it
is safe (every answer is asked afresh) but never replays.

## Changelog

- **2026-10-03**: A framework collection is hashed with what it holds beyond its elements: a
  wrapper (`Collection<T>`, a key view) as what it wraps, with its comparers, and a collection
  whose extra state cannot be read (`Hashtable`, `SortedList`, `BlockingCollection<T>`, an
  `ArrayList` wrapper, one that enumerates by its own code) gives no hash. A culture hashes only
  as the framework's shared instance, an enum's header holds its members, and a local time keeps
  its daylight-saving flag. A refused replay's reason names the state hash key when it is what
  differs.
- **2026-10-02**: The hash is keyed under a host's `StateHashKey` (`k1:`) and plain SHA-256
  otherwise (`s1:`); the encoding covers inline arrays, fixed buffers, lower bounds and every
  collection comparer, sorts unordered collections, names types with their assemblies and writes
  each once per hash; a refused replay's reason no longer quotes the answer.
- **2026-10-02**: The hash walks every field of the state's runtime types instead of hashing its
  JSON, so it covers the whole state; the fail-closed cases are listed.
- **2026-10-02**: Recorded.
