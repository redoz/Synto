---
name: handoff
description: Produce a dense, copy-pasteable handoff that lets a fresh, memoryless agent resume the current thread of work. Use when wrapping up a session, running low on context, pausing work, or handing the active task to a new agent window.
user_invocable: true
---

# handoff

Emit a single copy-pasteable block that lets a **fresh agent with no memory** resume the
**one task currently in flight** — nothing else from the session.

**Core principle — pointers over payloads.** A fresh agent has tools: it can read files,
run `git log`, read issues. So *point* to anything it can cheaply re-derive; spend tokens
only on what dies when this session ends — the decisions, the rationale, the approaches
already ruled out, and what the user said they want. That irreplaceable signal is the
handoff's real cargo. Everything else is a reference.

## When to use

- Context is running low and work must continue in a new window.
- Pausing a task to resume later or hand to another agent.
- The user says "hand this off", "write a handoff", "/handoff".

## Output

One fenced code block in the chat response, preceded by a one-line "paste this into a new
agent" preamble. **Write nothing to disk and commit nothing** — a stray handoff file on a
working branch is exactly what this avoids.

## Procedure

1. **Find the active thread.** Isolate the *one* task in flight. If genuinely ambiguous
   (two unrelated live threads), **ask the user one question** to pick before generating.
   Otherwise infer it.
2. **Fill the template** (below) from the session.
3. **Gap-check (private).** Ask yourself: *what would a newcomer need to ask to act
   correctly here?* Fold any missing answers into the template. These questions are
   scaffolding — they never appear in the output.
4. **Compress.** For every line apply the rule: *can a fresh agent cheaply re-derive this
   with its tools?* Yes → replace with a pointer (`path:line-range` + one line of why).
   No → keep it inline. Inline a short code/state excerpt **only** when the target is
   volatile or hard to locate. Drop anything that fails both "needed to act" and
   "can't recover".
5. **Emit** the block. Omit any empty section. Telegraph English — drop articles and
   filler, keep load-bearing nouns and verbs.

## Template

Emit exactly these sections (skip any that are empty):

```
# Handoff: <one-line task title>

## Goal
<the durable why — what success ultimately looks like>

## State
<done · in-flight · exactly where we stopped>

## Next move
<the single concrete first action — a command or edit, not a direction>

## Key locations
<path:line-range · issue # · branch · command — one pointer per line, each with why>

## Decided / rejected
<choices made + why · approaches tried and ruled out>

## Gotchas & scope guard
<constraints · do-NOT-do-X · things that bit us>

## Verify
<how the new agent knows it succeeded>

## First move
read <X>, run `git status`, then continue: <Y>
```

`Decided / rejected` and `Gotchas & scope guard` are the irreplaceable sections — weight
them. `Key locations` is where the pointer discipline pays off.

## Guard rails

- **No secrets in the block** — never inline `NUGET_API_KEY`, `GITHUB_TOKEN`, any
  `*_TOKEN` / `*_SECRET` / `*_KEY` value, bearer tokens, or raw payloads. Point to where
  they live instead.
- **Read-only** — generating a handoff reads the session and the repo; it writes no file
  and runs no mutating command.

## Common mistakes

| Mistake | Fix |
|---|---|
| Pasting file contents the new agent could just read | Point to `path:line-range` + one line of why |
| Recapping the whole session | Only the one active thread |
| Vague next step ("continue the refactor") | One concrete action — a command or a specific edit |
| Dropping the *why* behind a decision | That's the irreplaceable part — always inline it |
| Emitting the self-interrogation Q&A | It's private scaffolding; emit the filled template only |
