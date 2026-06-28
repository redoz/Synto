---
name: refresh-kb
description: Rebuild or update the OKF knowledge bundle under docs/kb — the concept-doc map of Synto's internals (each fact a path:line link) used for navigation. Reach for this whenever the kb has drifted from the code, or someone asks to "refresh the kb", "update the knowledge base", or "get the kb in sync" — e.g. after a feature lands, a subsystem is refactored, files move, a new concept needs seeding, or path:line references go stale. This is specifically the docs/kb concept map; it is NOT for editing .claude/playbook, writing READMEs, re-importing the OKF SPEC.md from upstream, or changing code or tests.
user_invocable: true
---

# refresh-kb

The `docs/kb/` bundle is an [OKF](../../../docs/kb/SPEC.md) knowledge map of Synto's
internals — the *where/what* layer (where code lives, the non-obvious invariants),
distinct from `.claude/playbook/` which owns the *why/how-to-evaluate*. This skill
keeps that map honest as the code moves.

**Core principle — it's a map, not prose.** Every fact points at code with a
`path:line` link and stays terse. Docs that explain instead of point rot into stale
walls; docs that point stay cheap to verify and cheap to trust. When in doubt, link
and trim rather than narrate.

## When to run, and how much

Decide scope first — most refreshes are *targeted*, not full rebuilds:

- **Targeted** (default) — a feature landed or a subsystem changed. Touch only the
  affected concept(s) + any index listing that references them. Don't rewrite the bundle.
- **Full** — the bundle is being seeded, or many areas drifted at once. Rebuild every
  concept from current source.
- **Drift-sweep** — paths/line numbers feel stale but the structure is fine. Re-verify
  `path:line` references and fix the drift; touch nothing else.

If the scope isn't obvious from the request, ask which of these it is before spawning work.

## The contract you must obey

The bundle is **self-describing** — its rules live inside it. Read these first, every run:

1. `docs/kb/conventions.md` — the **type registry** (fixed set of types + the rule for
   when a new one is allowed) and the **per-type frontmatter contract**. This is the
   source of truth; honor it over anything in this skill if they ever disagree.
2. `docs/kb/index.md` — the current shape, so you update rather than duplicate.

Non-negotiables that fall out of the contract:

- **Types are a fixed vocabulary.** Reuse an existing `type`. Add a new one *only* when
  a kind of concept recurs **≥3×** — otherwise you get sprawl no one can navigate. If a
  new type is justified, register it in `conventions.md` in the same change.
- **Every concept** (non-`index.md`) has frontmatter with a registry `type` plus its
  per-type required keys. **`index.md` files carry no frontmatter** (OKF §6).
- **Links are bundle-relative** (`/subsystems/templating.md`) — stable under moves.
  `resource` and inline `path:line` are **repo-root-relative** (root = repo top).
- **Body skeleton** for Project/Subsystem concepts: *Responsibility · Key files ·
  Entry points · Invariants · Related*. Keep each line a pointer.

## Gather accurate facts before writing

Never write a concept from memory — line numbers and structure drift, and a confidently
wrong map is worse than none. Get current facts from the actual tree:

- **Scope explorers to areas** and run them in parallel — e.g. one for projects +
  architecture, one for the templating pipeline, one for quoting/matching/decorations +
  examples. Ask each for dense `path:line`-backed facts (responsibility, key files,
  entry points, invariants, dependencies), not prose.
- **Ignore `.claude/workspaces/`** — those are throwaway jj workspace copies of `src/`
  and will pollute results. Only `src/`, `test/`, and `examples/` at the repo root count.
- For a targeted refresh you usually need just one explorer (or none, if you already
  have the facts in this session).

## Write / update concepts

For each concept in scope:

- Match the body skeleton above; lead with a one-line **Responsibility**.
- Back every claim with a `path:line` link. Cross-link related concepts so the map is
  traversable, not a pile of pages.
- After editing concepts, **update the listings**: the relevant domain `index.md` and
  the root `index.md` if you added/removed/renamed a concept. Index entries should carry
  the concept's one-line description so progressive disclosure actually discloses.

## Verify, then commit

1. **Conformance check** — every non-`index` `.md` in `docs/kb` has a registry `type`;
   `index.md` files have none. A quick grep for `^type:` across the bundle catches both
   a missing type and an accidental one in an index. (The imported `SPEC.md` is itself a
   `Reference` concept with frontmatter — keep it that way so the bundle passes OKF §9.)
2. **Sanity-check a few `path:line` links** you wrote actually point where you claim.
3. **Commit with jj** (this repo is jj-native): one focused commit, e.g.
   `docs(kb): refresh <area> — <what changed>`. Don't push unless asked.

## Maintenance truths

- **Paths are stable; line numbers are the soft part.** A drift-sweep that only fixes
  line numbers is a legitimate, cheap refresh — schedule it when concepts feel off.
- The kb is navigation, the playbook is judgment. If you're tempted to write *why a
  thing is good* or *how to review it*, that belongs in `.claude/playbook/`, not here.
