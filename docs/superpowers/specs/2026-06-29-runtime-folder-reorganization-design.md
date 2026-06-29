# Synto.Core runtime — folder reorganization (decoupled from namespaces)

**Date:** 2026-06-29
**Status:** Design — awaiting review. No code changed yet.
**Scope:** `src/Synto` (the `Synto.Core` runtime/toolkit package) only.

## Goal

The user-facing API deliberately uses **few namespaces** for discoverability
(`Synto`, `Synto.Templating`, `Synto.Matching`, `Synto.Formatting`, `Synto.Generators`).
That stays. But as a *developer* the flat layout hurts: 16 files sit loose in the project
root under one flat `namespace Synto`, and the runtime `Templating/` folder holds 18 files
in a single bucket.

This reorganization adds **feature/domain folders** that are **pure organization** — they do
**not** change any namespace. Folders mirror the namespaces we *would* have if discoverability
didn't argue against them. We organize by feature/domain, never by type-role (no
`Attributes/`, `Extensions/`, `Models/` buckets — that's the MVC anti-pattern we're avoiding).

This is safe to do for free: `.editorconfig` sets no `dotnet_style_namespace_match_folder`
rule and IDE0130 is off by default, so a file in `Synto/Quoting/` keeping `namespace Synto`
produces no warning. The `Synto.SourceGenerator` project already relies on exactly this
decoupling (every file there is `namespace Synto` regardless of folder).

## Decisions (locked with the user)

1. **Scope:** `src/Synto` only (the `Synto.Core` runtime). The `Synto.SourceGenerator`
   internal `Templating/` pile is a separate future effort.
2. **Folder↔namespace:** **fully decoupled.** Keep every current namespace exactly. New
   feature folders under the root keep `namespace Synto`.
3. **Reach:** carve the loose root **and** subdivide the runtime `Templating/` folder.
   Leave `Matching/`, `Formatting/`, `Generators/` as-is.
4. **Naming:** grounded in Roslyn's own plain vocabulary (`Syntax`, `Symbols`, `Emit`) plus
   established Synto domain terms (`Quoting`, `Decorations`, `Staging`) — no "look-at-me"
   names. See *Naming alternatives* for an optional Lisp-lens pass.

## Target layout

### Root — `namespace Synto` (5 feature folders)

```
src/Synto/
  Quoting/        CSharpSyntaxQuoter.cs, QuoteSyntaxExtensions.cs
  Decorations/    ClassDeclarationSyntaxExtensions.cs (WithAncestryFrom),
                  Identifier/Implements/Inherits/Sealed/VisibilityAttributeExtensions.cs
  Syntax/         Collection/Interpolation/Literal/SyntaxList/RuntimeTypeExtensions.cs
  Symbols/        SymbolExtensions.cs
  Emit/           GeneratedSourceFilenameExtensions.cs, UsingDirectiveSet.cs

  Formatting/     (unchanged — namespace Synto.Formatting)
  Generators/     (unchanged — namespace Synto.Generators)
  Matching/       (unchanged — namespace Synto.Matching)
  Templating/     (subdivided — see below)
```

### `Templating/` — `namespace Synto.Templating` (4 sub-folders)

```
src/Synto/Templating/
  Definition/     TemplateAttribute.cs, QuoteAttribute.cs, TemplateOption.cs
  Staging/        SpliceAttribute.cs, UnquoteAttribute.cs, RuntimeAttribute.cs,
                  Template.Parameter.cs (the Template facade), Syntax.cs (Syntax/Syntax<T> delegates)
  Builders/       SyntaxBuilderAttribute.cs, QuotedAttribute.cs, ReturnTypeAttribute.cs, SyntoBuilders.cs
  Decorations/    IdentifierAttribute.cs, ImplementsAttribute.cs, InheritsAttribute.cs,
                  SealedAttribute.cs, VisibilityAttribute.cs, Access.cs
```

Note `Decorations/` appears on **both** sides — the runtime appliers (`namespace Synto`,
`Apply*Attribute`) and the marker attributes (`namespace Synto.Templating`). That is the
honest picture: one feature, two namespaces, deliberately not merged (the appliers must be in
`namespace Synto` so the extension methods resolve without an extra `using`).

## The one real hazard: path-based cross-project references

Namespaces, file contents, and embedded-resource **LogicalNames stay identical**, so the
generated output must be **byte-for-byte unchanged**. The *only* thing that can break the
build is a stale **file path** — because every cross-project reference into `src/Synto` is
**by path**. There are exactly **31** such references to update, in lockstep with the moves.

### `Synto.csproj` — 1 edit (`<Compile Remove>`, path relative to `src/Synto`)

| Old | New |
|---|---|
| `Templating\SyntoBuilders.cs` | `Templating\Builders\SyntoBuilders.cs` |

(The other `<Compile Remove>` entries — `Matching\*`, `Generators\Interceptors.cs` — point at
folders we are **not** touching, so they stay.) SDK-style globbing auto-includes the moved
files at their new paths; this `Remove` must follow the move or `SyntoBuilders` would compile
back into `Synto.Core` and collide.

### `Synto.Bootstrap.csproj` — 2 edits (`<Compile Include ... Link>`)

| Old | New |
|---|---|
| `..\Synto\LiteralSyntaxExtensions.cs` | `..\Synto\Syntax\LiteralSyntaxExtensions.cs` |
| `..\Synto\UsingDirectiveSet.cs` | `..\Synto\Emit\UsingDirectiveSet.cs` |

(`..\Synto\Formatting\SyntaxFormatter.cs` is unchanged.)

### `Synto.SourceGenerator.csproj` — 28 edits (`<EmbeddedResource Include>`, **LogicalName unchanged**)

**Templating group (18)** — add the new sub-folder segment:

| Old `Include` | New `Include` |
|---|---|
| `..\Synto\Templating\TemplateAttribute.cs` | `..\Synto\Templating\Definition\TemplateAttribute.cs` |
| `..\Synto\Templating\QuoteAttribute.cs` | `..\Synto\Templating\Definition\QuoteAttribute.cs` |
| `..\Synto\Templating\TemplateOption.cs` | `..\Synto\Templating\Definition\TemplateOption.cs` |
| `..\Synto\Templating\SpliceAttribute.cs` | `..\Synto\Templating\Staging\SpliceAttribute.cs` |
| `..\Synto\Templating\UnquoteAttribute.cs` | `..\Synto\Templating\Staging\UnquoteAttribute.cs` |
| `..\Synto\Templating\RuntimeAttribute.cs` | `..\Synto\Templating\Staging\RuntimeAttribute.cs` |
| `..\Synto\Templating\Template.Parameter.cs` | `..\Synto\Templating\Staging\Template.Parameter.cs` |
| `..\Synto\Templating\Syntax.cs` | `..\Synto\Templating\Staging\Syntax.cs` |
| `..\Synto\Templating\SyntaxBuilderAttribute.cs` | `..\Synto\Templating\Builders\SyntaxBuilderAttribute.cs` |
| `..\Synto\Templating\QuotedAttribute.cs` | `..\Synto\Templating\Builders\QuotedAttribute.cs` |
| `..\Synto\Templating\ReturnTypeAttribute.cs` | `..\Synto\Templating\Builders\ReturnTypeAttribute.cs` |
| `..\Synto\Templating\SyntoBuilders.cs` | `..\Synto\Templating\Builders\SyntoBuilders.cs` |
| `..\Synto\Templating\IdentifierAttribute.cs` | `..\Synto\Templating\Decorations\IdentifierAttribute.cs` |
| `..\Synto\Templating\ImplementsAttribute.cs` | `..\Synto\Templating\Decorations\ImplementsAttribute.cs` |
| `..\Synto\Templating\InheritsAttribute.cs` | `..\Synto\Templating\Decorations\InheritsAttribute.cs` |
| `..\Synto\Templating\SealedAttribute.cs` | `..\Synto\Templating\Decorations\SealedAttribute.cs` |
| `..\Synto\Templating\VisibilityAttribute.cs` | `..\Synto\Templating\Decorations\VisibilityAttribute.cs` |
| `..\Synto\Templating\Access.cs` | `..\Synto\Templating\Decorations\Access.cs` |

**Helper group (10)** — root files into their new feature folders:

| Old `Include` | New `Include` |
|---|---|
| `..\Synto\RuntimeTypeExtensions.cs` | `..\Synto\Syntax\RuntimeTypeExtensions.cs` |
| `..\Synto\LiteralSyntaxExtensions.cs` | `..\Synto\Syntax\LiteralSyntaxExtensions.cs` |
| `..\Synto\CollectionSyntaxExtensions.cs` | `..\Synto\Syntax\CollectionSyntaxExtensions.cs` |
| `..\Synto\InterpolationSyntaxExtensions.cs` | `..\Synto\Syntax\InterpolationSyntaxExtensions.cs` |
| `..\Synto\QuoteSyntaxExtensions.cs` | `..\Synto\Quoting\QuoteSyntaxExtensions.cs` |
| `..\Synto\IdentifierAttributeExtensions.cs` | `..\Synto\Decorations\IdentifierAttributeExtensions.cs` |
| `..\Synto\ImplementsAttributeExtensions.cs` | `..\Synto\Decorations\ImplementsAttributeExtensions.cs` |
| `..\Synto\InheritsAttributeExtensions.cs` | `..\Synto\Decorations\InheritsAttributeExtensions.cs` |
| `..\Synto\SealedAttributeExtensions.cs` | `..\Synto\Decorations\SealedAttributeExtensions.cs` |
| `..\Synto\VisibilityAttributeExtensions.cs` | `..\Synto\Decorations\VisibilityAttributeExtensions.cs` |

(The `Matching\*` (8) and `Generators\Interceptors.cs` embedded resources, and the local
`EquatableArray.cs` / `DiagnosticInfo.cs`, are unchanged.)

Two files are referenced from **two** projects and so appear in two edit sets:
`LiteralSyntaxExtensions.cs` (Bootstrap `Compile` + SourceGenerator `EmbeddedResource`) and
`SyntoBuilders.cs` (Synto `Compile Remove` + SourceGenerator `EmbeddedResource`).

### Untouched
`Synto.Diagnostics.csproj` references `src/Synto` only via `ProjectReference` (no per-file
paths). No test project references `src/Synto` files by path. `.slnx` lists projects, not
files. `Synto.Bootstrap/CSharpSyntaxQuoter.cs` is a separate hand-maintained copy that
Bootstrap does **not** link from `src/Synto`, so moving the runtime quoter to `Quoting/` does
not touch the self-host chain (the sync invariant is content-based, not path-based).

## What does NOT change (the safety invariant)

- **No namespace changes** — anywhere, in any file.
- **No file-content changes** — files move; bytes are identical (LF preserved by
  `.gitattributes`/`.editorconfig`).
- **No `LogicalName` changes** — every embedded-resource name is preserved, so
  `HelperResourceLoader` / `SurfaceInjectionGenerator` / the `Synto.Runtime.*` and
  `Synto.Helper.*` lookups and the generator `TrackingNames` all resolve unchanged.
- **Generated output and all verified snapshots are byte-identical.** A snapshot diff is a
  regression to investigate, **not** to re-accept.

## Verification

1. `dotnet build` (Release) the whole solution — exercises the self-host chain
   (Bootstrap → Synto → SourceGenerator). Must be green; a missed path surfaces here as a
   missing `Compile`/`EmbeddedResource` item.
2. Run all test projects (`Synto.Test`, `Synto.Bootstrap.Test`, `Synto.Diagnostics.Test`) and
   the `Synto.Example.ObjectReader.Tests` — all green.
   (Per project convention, invoke MTP raw: `dotnet test --project <csproj>`.)
3. **Snapshot guard:** zero snapshot diffs expected. Any diff → stop and investigate.
4. **Stale-path guard:** after edits, grep the three csproj files for any `Include="..\Synto\…"`
   / `Remove="Templating\…"` still pointing at a pre-move path. (The build is the real gate;
   this is a fast pre-build sanity check.)
5. Confirm `git`/`jj` tracks each file at its new path (jj tracks by content automatically).

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Missed path reference → build break | All 31 enumerated above; build is a hard gate. |
| `SyntoBuilders`/removed files compile back into `Synto.Core` (CS0121 collision) | Update the `<Compile Remove>` path in lockstep with the move. |
| Snapshot churn | None expected; treat any diff as a real regression, not a re-accept. |
| `docs/kb` `path:line` links go stale | Run the `refresh-kb` skill afterward (follow-up, non-blocking). |

## Out of scope

- Any namespace addition/removal/rename.
- `Matching/`, `Formatting/`, `Generators/` folders (left flat).
- The `Synto.SourceGenerator` internal `Templating/` pile (~45 files) — a separate effort.
- Any logic/code change. This is a pure move + csproj path fix-up.

## Naming alternatives (Lisp/Scheme lens — optional, decide at review)

Synto's quote/unquote/splice surface *is* Lisp quasiquotation, so these are fair game if you
prefer them over the Roslyn-grounded names:

- `Templating/Staging/` → `Templating/Unquoting/` or `Templating/Quasiquotation/`
  (the group is the unquote family: `[Unquote]` ≈ `,`, `[Splice]` ≈ `,@`, `Parameter`/`[Runtime]`
  feed values into the quote).
- `Templating/Definition/` → `Templating/Macro/` (a `[Template]` is the `defmacro`).

Recommendation: keep `Staging` (it mirrors the generator's `Staged*` vocabulary, giving
runtime↔generator symmetry) and `Definition`. Everything else (`Quoting`, `Decorations`,
`Syntax`, `Symbols`, `Emit`, `Builders`) stays.

## Follow-up after implementation

- Run `refresh-kb` to repair `docs/kb` `path:line` links touched by the moves.
