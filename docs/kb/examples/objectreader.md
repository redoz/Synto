---
type: Example
title: ObjectReader
description: Compile-time, zero-reflection IDataReader generation via interceptors — Synto's primary dog-food friction probe.
resource: examples/Synto.Example.ObjectReader
projects: [Api, Generator, Demo, Tests]
drives: [child templates, staged scalar member-access fold, interpolation staged-fold]
tags: [example, dogfood, interceptors]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

A realistic consumer: turn `ObjectReader.Create(items, "Name", "Age")` into a
per-call-site specialized `IDataReader` with **no runtime reflection**. It exists
to surface friction — its rough edges are mined for new Synto features (e.g.
child templates, the staged scalar member-access fold, interpolation staged-fold).

# Sub-projects

- **Api** — `Synto.Example.ObjectReader.Api/ObjectReader.cs:11`; `Create<T>(source,
  members)` (`:14`) throws at runtime unless intercepted (the constant-member surface).
- **Generator** — `…Generator/ObjectReaderGenerator.cs:25`; `Initialize` (`:36`),
  `IsCandidate` (`:49`) detects `ObjectReader.Create()` call sites and emits a
  specialized reader + `[InterceptsLocation]` interceptor.
- **Demo** — `…Demo/Program.cs:9`; end-to-end usage over `Person` records.
- **Tests** — `…Tests/`: API, behavior, snapshot, incremental, and diagnostics
  suites (including the non-constant `SOR0002` runtime-fallback path).

# Invariants

- Showcase refactors require **semantic equivalence + green tests**, not
  byte-identical snapshots; benign snapshot re-accepts are fine.
- Exercises the [incremental cacheability contract](/architecture/incremental-pipeline.md)
  (it has a dedicated incremental test suite).

# Related

- [Templating](/subsystems/templating.md) — the features it drives.
