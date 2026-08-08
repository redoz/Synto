# Synto.Core public-surface audit — bless or internalize, and pin it (#20)

**Status: DRAFT / HELD for owner review.** Carved out of `2026-07-04-issues-triage-fixes.md` (issue #20) at the owner's request, because it is a **1.0 one-way-door decision** — which public types Synto.Core freezes at first publish — that wants explicit sign-off before it lands. Promote to `docs/superpowers/plans/` (top level) to implement.

**Goal:** Make Synto.Core's *actual* exported surface match a **decided, pinned** contract, and lock it with a reflection test so accidental widening fails a test rather than a review.

## Context

`src/Synto/Synto.csproj:58–61` comments claim "only the 5 markers + 3 emitted helpers are public surface," but ~30 public types actually ship: 10 helper classes (`LiteralSyntaxExtensions`, `RuntimeTypeExtensions`, `QuoteSyntaxExtensions`, `CollectionSyntaxExtensions`, `InterpolationSyntaxExtensions`, the 5 `Apply*AttributeExtensions`) plus the Matching pattern markers. `FileLocalHelpers` injects *file-scoped copies* of the helpers into consumer output, so the public copies are a duplicate surface that would freeze at 1.0.

## The decision the owner should make first

The proposed **blessed public list** (everything else internalized):
- Consumer markers: `TemplateAttribute`, `QuoteAttribute`/`UnquoteAttribute`/`SpliceAttribute`, `RuntimeAttribute`, `TemplateOption`, `Syntax`/`Syntax<T>`, and the builder/decoration/matching **marker attributes**.
- Three blessed emitted-helper classes: `LiteralSyntaxExtensions` (`ToSyntax`), `RuntimeTypeExtensions` (`ToTypeSyntax`), `QuoteSyntaxExtensions` (`OrNullLiteralExpression`).
- Internalized unless the audit finds a hard dependency: `CollectionSyntaxExtensions`, `InterpolationSyntaxExtensions`, `SyntaxListExtensions`, the 5 `Apply*AttributeExtensions`, … (the `InternalsVisibleTo` grants to `Synto.SourceGenerator`, `Synto.Diagnostics`, `Synto.Test` already cover the legitimate internal consumers).

**Open question for the owner:** is any currently-public helper part of an intended *consumer* API (e.g. do you want users writing `x.OrNullLiteralExpression()` or `CollectionSyntaxExtensions` directly)? Anything answered "yes" is blessed explicitly rather than internalized.

## Files (when implemented)
- Modify: the `src/Synto/**` helper classes chosen for internalization (accessibility keyword only).
- Modify: `src/Synto/Synto.csproj` (:58–61 comment — must match the audited reality).
- Possibly modify: the `FileLocalHelpers`/`SurfaceInjectionGenerator` accessibility-rewrite step (see checks).
- Test: new `test/Synto.Test/PublicSurfaceTest.cs`.

## Interfaces (contract)
- **Pin test:** `PublicSurfaceTest` reflects over `Synto.dll`'s exported types and asserts set-equality with the blessed list — future accidental widening fails a test, not a review.
- **Mandatory audit checks** (each may convert an "internalize" into an explicit "bless", recorded in the csproj comment):
  - (a) the `FileLocalHelpers`/injection rewriter's `public`→`file` (and `public`→`internal`) rewrite must handle the authored accessibility of every embedded source it processes — if a newly-internal helper is embedded, either keep that *file* authored public (blessing it) or extend the rewriter's contract to accept `internal`.
  - (b) `DecorationFinder`'s `Apply*` hook resolution must still resolve the built-in decoration hooks after `Apply*AttributeExtensions` are internalized (they resolve inside the generator, which has `InternalsVisibleTo` — verify, don't assume).
  - (c) the csproj comment is updated to enumerate the final blessed list either way.
- **Outcome flexibility:** if a type cannot be safely internalized now, blessing it *explicitly* (comment + pin test) still satisfies #20 — the deliverable is a decided, pinned surface, not a maximal internalization.

## Steps (when implemented)
1. Write the failing `PublicSurfaceTest` pinning the intended blessed list (fails today: ~30 exported types vs the blessed set).
2. Internalize the non-blessed types; run the audit checks (a)–(c); adjust the blessed list only with a recorded reason.
3. Update the csproj comment to the final list.
4. Full gate: `Synto.Test` + `Synto.Diagnostics.Test` + `Synto.Bootstrap.Test` (bootstrap `<Compile Link>`s some of these sources — accessibility edits must keep it compiling).
5. Format clean (`dotnet format whitespace --verify-no-changes`).
6. `jj commit -m "refactor(surface): audit+pin Synto.Core public surface; internalize non-blessed helpers (#20)"`.

## Global constraints
jj VCS (not git); tests via raw `dotnet test --project <csproj>` (never through rtk); snapshot accept with `$env:DiffEngine_Disabled='true'` then overwrite `.verified` with `.received`; incremental cacheability preserved; format-whitespace gate clean; no git hooks.
