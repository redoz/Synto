---
type: Reference
title: KB conventions & type registry
description: The fixed type vocabulary and per-type frontmatter contract for this OKF bundle.
tags: [meta, okf, conventions]
timestamp: 2026-06-28T00:00:00Z
---

This bundle is an [OKF](/SPEC.md) knowledge bundle that maps Synto's internals
**for navigation** — "what is this, where does it live, what does it depend on,
what are the non-obvious invariants." It is the *map*; the
`.claude/playbook/` is the *judgment* (principles, review standards). Keep the
boundary firm and cross-reference rather than duplicate.

# Domains

Top-level groups. Each is a directory with its own `index.md`.

| Domain | Holds | Type used |
|--------|-------|-----------|
| *(root)* | bundle entry + self-description | `Reference` |
| `architecture/` | cross-cutting structure & mechanisms | `Architecture` |
| `projects/` | the buildable assemblies under `src/` | `Project` |
| `subsystems/` | functional code areas (span files within a project) | `Subsystem` |
| `examples/` | dog-food / worked examples | `Example` |

# Type registry

A **fixed** set of five `type` values. Add a new type only when a kind of
concept recurs **three or more times** — otherwise reuse the closest existing
type. This keeps the vocabulary navigable instead of sprawling into single-use
labels.

| `type` | Meaning | Lives in |
|--------|---------|----------|
| **Project** | A buildable assembly (a `.csproj`). | `projects/` |
| **Subsystem** | A functional area of the code, spanning files within one project. | `subsystems/` |
| **Architecture** | A cross-cutting structural fact or mechanism (layering, bootstrap loop, the incremental pipeline). | `architecture/` |
| **Example** | A worked / dog-food example bundle. | `examples/` |
| **Reference** | Meta docs: this file, the glossary, the imported spec. | *(root)* |

# Frontmatter contract

Base keys on **every** concept (OKF §4.1): `type` (required), `title`,
`description`, `tags`, `timestamp` (ISO 8601). `index.md` files carry **no**
frontmatter (OKF §6). Per-type additions:

- **Project** — `resource` (repo-root-relative path to the project dir),
  `layer` ∈ {`runtime`, `generator`, `bootstrap`, `diagnostics`}; optional
  `depends_on` (list of project names), `emits` (list of generated artifacts).
- **Subsystem** — `resource` (repo-root-relative dir path), `project` (owning
  Project concept id); optional `entrypoints` (list of `path:line` / `#Symbol`).
- **Architecture** — optional `relates` (list of concept ids),
  `status` ∈ {`stable`, `evolving`}.
- **Example** — `resource` (repo-root-relative dir path); optional `projects`
  (list of sub-projects), `drives` (Synto features the example has motivated).
- **Reference** — optional `resource` (URL or path).

# Body shape (Project / Subsystem)

For code concepts, favor a fixed skeleton so docs stay a map and don't rot into
prose walls:

- **Responsibility** — one line.
- **Key files** — `path:line` links, terse.
- **Entry points** — where execution starts.
- **Invariants** — the non-obvious contracts (cacheability, layering, etc.).
- **Related** — cross-links.

# Link & path conventions

- Cross-links use **bundle-relative** form (`/subsystems/templating.md`), per
  OKF §5.1 — stable under moves.
- `resource` and inline `path:line` references are **repo-root-relative**
  (root = `C:\dev\Synto`), e.g. `src/Synto.SourceGenerator/Templating`.
- Broken links are tolerated (OKF §5.3) — they mark not-yet-written concepts.
