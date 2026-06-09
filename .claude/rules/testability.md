# Testability

Evaluate whether the code is structured for effective testing and is actually tested.

## Checklist

### Structure
- Is effectful code separated from pure logic? The pipeline should be **pure, equatable
  transforms** (`SyntaxNode` → `TemplateInfo` → emitted source) that can be exercised
  without a full build; emission is a pure function of the equatable model.
- Are external dependencies injectable or abstractable? The Roslyn `Compilation` is
  provided through a `CSharpGeneratorDriver` harness (`DiagnosticsGeneratorTest.GeneratorDriver`
  builds a `CSharpCompilation` from source text + metadata references and runs the
  generator) — tests don't need MSBuild or a project system.
- Are module boundaries clean enough to test in isolation? (the quoter round-trip via
  in-process `[Template]` local functions in `RoundTripTests`; the diagnostics generator
  via a driver + Verify; the bootstrap quoter via its own snapshot)
- Are there implicit dependencies that make tests fragile? (line-ending sensitivity —
  `RoundTripTests.AssertGenerated` normalizes CRLF/LF and renders with `eol:"\n"`;
  absolute paths or machine-specific metadata leaking into snapshots; ambient assembly-load
  order — exactly what the `Synto.Diagnostics.Test` runtime-load failure exposed under MTP)

### Coverage
- Is there coverage for the load-bearing paths? The critical ones: **generated-output
  snapshots** (the golden `*.verified.cs` files), the **quoter round-trip**
  (`AssertGenerated` over each `TemplateOption` shape), **incremental cacheability /
  equatability** of the pipeline models, **diagnostics emission** (`SY0000` and
  `SY1001`–`SY1007`), and the **self-host bootstrap** (the `CSharpSyntaxQuoter` snapshot
  proving the generator reproduces its own checked-in output).
- Is there a test that asserts **cache hits / equatability** — re-running the driver on an
  unrelated edit yields cached steps? This is the easiest correctness property to regress
  and the easiest to leave untested.
- Are existing tests meaningful? (asserting **rendered output / round-trips / emitted
  diagnostics** — behavior — rather than implementation details)
- Are tests reliable? (no ordering or timing dependence; Verify snapshots byte-stable
  across platforms; whitespace normalized; no flaky assertions)

### Infrastructure
- Is the test infrastructure adequate? **Verify** snapshot tests (golden `*.verified.cs`
  under `snapshots/`, configured via `ModuleInitializer`), a `CSharpGeneratorDriver` over an
  in-memory `CSharpCompilation`, run under **Microsoft Testing Platform v2** with **xUnit
  v3** (~35 tests).
- Are there integration points only testable end-to-end that should have a unit test (a
  single node-kind's emission), or vice versa?
- When a snapshot changes, is it obvious whether to accept the new `.received.cs` or fix a
  regression? Are goldens reviewed, not blindly accepted?
- Could a new contributor write a test for a bug fix (a new template shape, a new
  diagnostic) by copying an existing `[Fact]` without understanding the whole generator?

## Scope Guidance

- **Full evaluation**: Review snapshot coverage across template shapes and diagnostics, the
  round-trip and cacheability/equatability tests, the bootstrap snapshot, and the driver
  harness / Verify configuration.
- **Change review**: Focus on whether new emission is snapshot-covered, new diagnostics have
  a driver test, new pipeline stages have an equatability/cache-hit test, and whether the
  change is testable without a real build.
