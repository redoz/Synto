# Port contract — OuroCore (Rust) → Synto (C#/.NET) skill system

> TEMPORARY FILE. You are one of several agents porting the Claude Code skill system from
> `C:\dev\ourocore\.claude\` (Rust / "OuroCore") into `C:\dev\Synto\.claude\` (C#/.NET / "Synto").
> **Faithfully PORT** each source file: preserve its structure, logic, prose quality, prompt wording,
> and level of detail. Do NOT invent a new system or paraphrase loosely — the value is in the
> carefully-designed originals. Apply the global transforms below uniformly. Read your assigned
> ourocore source file(s) first, then write the Synto version to the target path(s) you were given.

## 1. Project identity

- Product: OuroCore (a Rust CI/CD-observability **service**) → **Synto** — "SYNtax TOolkit": Roslyn
  **incremental source generators** for quoting/templating C# syntax trees. Templating is the first
  feature; Matching is next; more later.
- GitHub repo: `OuroborosFoundation/OuroCore` → **`redoz/Synto`**.
- Local path constant `C:/dev/OuroCore` → **`C:/dev/Synto`**.
- GitHub Project board name "OuroCore — Issue Flow" → **"Synto — Issue Flow"**.

## 2. Toolchain (Rust → .NET 10)

| OuroCore (Rust) | Synto (.NET) |
|---|---|
| `cargo build --workspace --all-targets` | `dotnet build --no-restore -c Debug` (solution `Synto.slnx`) |
| `cargo nextest run` / `cargo test --workspace` | `dotnet test --no-build -c Debug` (Microsoft Testing Platform v2 runner, set in `global.json`; xUnit v3 + Verify snapshot tests; ~35 tests) |
| `cargo fmt --check` | `dotnet format --verify-no-changes` |
| `cargo clippy --all-targets -- -D warnings` | analyzer warnings surfaced by `dotnet build` — **NOTE: `TreatWarningsAsErrors` is NOT set in Synto**, so analyzer warnings are *findings*, not hard green-gate failures |
| `cargo test --doc` | (no equivalent — drop it) |

**DELETE all infrastructure content.** Synto has **NO database, NO containers, NO network runtime** —
it is a build-time library + source generators. Remove every reference to Postgres, `#[sqlx::test]`,
`pg_isready`, podman, docker, `infra-postgres-1`, `docker-compose`, and any "bring up infra…"
remediation text.

## 3. Drop the delivery persona (explicit user requirement)

- No `delivery.md`. Remove the `delivery` / `delivery-expert` reviewer from every REVIEWERS array and
  every dimension enumeration. Remove the delivery-expert row from `standards.md`.
- Remove ALL Dave Farley / DORA / deployment-frequency / lead-time / change-failure-rate /
  CI-provider-modeling / "golden run" references.

## 4. Trim the review dimensions (user chose "fitting ones only")

Canonical set for Synto:

- **4 review dimensions**: `correctness`, `maintainability`, `performance`, `testability`.
- **1 domain expert**: `principal-engineer` (rules file `consequences.md`).
- **DROP entirely**: `security.md`, `resilience.md`, `observability.md` — and their reviewers,
  enumerations, and quality-gate rows. No security / resilience / observability dimension anywhere.

Canonical REVIEWERS array — use this exact shape in `evaluate.js` and `issue-review.js`:

```js
const REVIEWERS = [
  { key: 'correctness',     role: 'correctness-reviewer', rules: '.claude/rules/correctness.md' },
  { key: 'maintainability', role: 'architect',            rules: '.claude/rules/maintainability.md' },
  { key: 'performance',     role: 'performance-reviewer', rules: '.claude/rules/performance.md' },
  { key: 'testability',     role: 'quality-engineer',     rules: '.claude/rules/testability.md' },
  { key: 'consequences',    role: 'principal-engineer',   rules: '.claude/rules/consequences.md', expert: true },
];
```

The single-agent spec-review path (issue-review.js) reads exactly those 5 rule files.

`standards.md` quality-gate tiers, re-derived for the trimmed set:
- **Hard gate** (one critical ⇒ fail): **Correctness**.
- **Must be "Good enough" or better**: **Maintainability**, **Performance**.
- **Must be "Needs improvement" or better** (not Broken): **Testability**.
- Plus the `principal-engineer` (consequences) cross-cutting lens.

## 5. Synto domain facts (for re-aiming architecture / principles / dimension rules)

- **Incremental source generators** (`IIncrementalGenerator`). The #1 correctness *and* performance
  concern is **cacheability**: the pipeline model must be **equatable value types** and must NOT
  capture `Compilation`, `ISymbol`, or `SyntaxNode` into pipeline state, or incremental caching breaks
  (the generator re-runs on every keystroke). The recent commit "Refactor template generator to
  equatable pipeline model" is exactly this discipline.
- Generated-code correctness is verified by **Verify snapshot tests** (golden `*.verified.cs` files)
  plus a `CSharpGeneratorDriver` test harness.
- Projects under `src/` (only two ship):
  - `src/Synto` — runtime/toolkit surface (marker attributes, `TemplateOption`, `Syntax`/`Syntax<T>`,
    helpers like `OrNullLiteralExpression`, `ToTypeSyntax`) → ships as package **`Synto.Core`**, `netstandard2.0`.
  - `src/Synto.SourceGenerator` — the **umbrella generator** ("SYNtax TOolkit"), all features as
    folders/namespaces (`Templating/` today, `Matching/` next) → ships as package **`Synto`**.
  - `src/Synto.Bootstrap` — self-host generator used to build Synto itself (not shipped).
  - `src/Synto.Diagnostics` — diagnostics generator → ships as separate dev-only package **`Synto.Diagnostics`**.
- **Packaging = source-injection.** The `Synto` generator injects the small consumer-facing surface as
  **source** via `RegisterPostInitializationOutput` (markers `TemplateAttribute`/`InlineAttribute`/
  `RuntimeAttribute`/`TemplateOption`/`Syntax`/`Syntax<T>` + the 2 emitted helpers
  `OrNullLiteralExpression`, `ToTypeSyntax`), as `internal`, file-scoped namespaces — so consumer
  generated code has **no runtime package dependency**. Generator-internal helpers (`CSharpSyntaxQuoter`,
  `SyntaxFormatter`, `UsingDirectiveSet`, `SymbolExtensions`, list extensions) never appear in consumer
  output. `Synto.dll` is injected next to each generator under `analyzers/dotnet/cs` (Roslyn does not
  load an analyzer's NuGet deps into the analyzer load context).
- Tech baseline: **.NET 10 SDK**, **netstandard2.0** (generators + runtime), **Roslyn 5.0** baseline,
  central package management (`Directory.Packages.props`), shared packaging logic in
  `Directory.Build.targets` gated on `<IsSyntoGeneratorPackage>`, **MTP v2** test platform, **jj
  (Jujutsu)** over the git repo (git commands still work; the repo may sit on a detached HEAD),
  self-host bootstrap chain (`src/Synto` consumes `Synto.Bootstrap` as an analyzer).
- **Maintainability load-bearing concern** = the **layering** (consumer-facing surface vs
  generator-internal helpers; runtime vs generator) and a **single source of truth for the injected
  helpers** (today some quoter/formatter sources are duplicated as linked files between `src/Synto` and
  `src/Synto.Bootstrap` — a known smell to flag, not yet fixed).

## 6. Secret scrubbing

The `sanitize()` scrubber and any "never log secrets" guidance must target Synto's secrets, not
OuroCore's. Replace `webhook_secret` / `OUROCORE_INGEST_TOKEN` / `DATABASE_URL` with:
`NUGET_API_KEY`, `GITHUB_TOKEN`, and generic `*_TOKEN` / `*_SECRET` / `*_KEY` / bearer-token patterns.

## 7. Keep ALL issue-flow machinery intact

Labels (`status:*`, `blocked`, `manual`, `epic`, `issue-flow-bug`), the H2-comment convention and all
comment templates, the driver / reconcile / commit-gate / unaddressed-comment-guard / round-limit-K
semantics, `set-status`, throwaway worktrees, and the green-gated fast-forward-push-to-`main` implement
model all port unchanged (only the toolchain commands inside them change per §2). The `manual` brake
remains the human approval gate.

## 8. The infra probe

Synto has no infrastructure, so `probe` only checks that local `main` is **fast-forwardable vs
`origin/main`** (keep that git logic exactly). Keep the `--implement` / `--no-implement` flag accepted
for caller compatibility, but it no longer checks any database. Remove all Postgres/podman remediation
text.
