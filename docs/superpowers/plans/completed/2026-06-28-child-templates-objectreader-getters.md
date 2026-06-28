# Child Templates (Narrow: ObjectReader Typed-Getter Collapse) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse ObjectReader's 12 near-identical typed-getter method declarations (`GetBoolean`…`GetString`) into ONE child `[Template]` getter invoked once per getter by a `[Splice]` member-generator — proving the "a template invoking a template" composition pattern end-to-end, with **byte-identical** generated output (behavior-preserving).

**Architecture:** A *standalone* child `[Template]` method `TRet TypedGetter<[Splice] TRet>(int i)` surfaces as `Factory.TypedGetter(TypeSyntax TRet, EquatableArray<ColumnInfo> columns, string clrTypeName, string typeLabel)` returning a `MethodDeclarationSyntax`. A `[Splice] static IEnumerable<MemberDeclarationSyntax> TypedGetters()` member-generator (in the existing reader carrier) calls that child factory once per getter, supplying the return-type `TypeSyntax`, the type-name filter, and the exception-label, then renames each result via post-quote `.WithIdentifier(Identifier("GetXxx"))`. The child is authored standalone with an inert `_e` field so it compiles alone and needs **no nesting/exclusion support** from the generator; `_e.Current` quotes to the same `_e.Current.<member>` access the real reader exposes.

**Tech Stack:** C# / Roslyn incremental source generators; xUnit v3 + Microsoft.Testing.Platform; Verify (`Verify.SourceGenerators`, `Verify.XunitV3`) snapshots; jj (use `rtk jj`).

## Global Constraints

- **No generator changes anticipated.** The narrow case is pure composition on already-shipped substrate (`[Splice]` type params on a `[Template]` method — proven by `SimpleTemplateTest.SpliceGenericType`; `[Splice]` member-generators returning `IEnumerable<MemberDeclarationSyntax>` — shipped 2026-06-28, `SpliceMemberGeneratorFinder` + `TemplateFactorySourceGenerator.cs:1015`). Task 1 is the empirical gate; if it surfaces a real gap, the only plausible fix sites are the `Member<>` rewriter and `StagedTypeParameterFinder.cs` — escalate to systematic-debugging, do **not** widen scope silently.
- **Behavior-preserving.** The ObjectReader snapshot `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs` MUST stay byte-identical after the migration. The behavior tests (`ObjectReaderBehaviorTests`) and the cacheability guard (`ObjectReaderIncrementalTests`) MUST stay green.
- **Cacheability.** Child-factory arguments must be equatable data only: `TypeSyntax` (built at factory-build time), `EquatableArray<ColumnInfo>`, `string`. No `ISymbol`/`SemanticModel` enters the incremental model — the model stays `ColumnInfo`-based exactly as today (`Model.cs`).
- **Generator carriers target `netstandard2.0`.** The live surface (`Parameter<>`, `Member<>`, `TypeOf`) is inert at carrier-compile (`return default!`); `foreach`/`.Where(...)` are ordinary C# the generator unrolls at factory-build time. Everything authored must compile as real C# in the `netstandard2.0` generator assembly.
- **Names via `.WithIdentifier(...)` only** — no identifier hole (resolved decision). **Out of scope (deferred):** inline `Splice(Factory.X(...))` graft, and position-aware `Splice` validation. Do not build either.
- **No git pre-commit hooks.** Format via `dotnet format whitespace` + CI; do not add hooks.
- The "13 getters" in the spec/handoff is approximate — the carrier has exactly **12** typed getters (`GetBoolean, GetByte, GetChar, GetDateTime, GetDecimal, GetDouble, GetFloat, GetGuid, GetInt16, GetInt32, GetInt64, GetString`). This plan migrates those 12.

---

## File Structure

- **Create** `test/Synto.Test/Templating/ChildTemplateTest.cs` — the durable generic proof of the composition pattern (Task 1).
- **Create** `test/Synto.Test/Templating/snapshots/ChildTemplateTest.*.verified.cs` — accepted snapshot(s) for Task 1.
- **Create** `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/GetterTemplate.cs` — the standalone child `[Template]` getter (Task 2).
- **Modify** `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` — delete the 12 getter methods, add the `[Splice] TypedGetters()` member-generator in their place (Task 3).
- **Unchanged but load-bearing as the gate:** `examples/.../Synto.Example.ObjectReader.Tests/snapshots/...#ObjectReader.g.verified.cs`, `ObjectReaderBehaviorTests.cs`, `ObjectReaderIncrementalTests.cs`.
- **Modify** `docs/superpowers/specs/2026-06-28-child-templates-design.md` (status → narrow case shipped) and `MEMORY.md` pointer (Task 4).

## The getter data (the irreducible per-getter table)

Each row = (Identifier, return-type `TypeSyntax`, filter string, exception label). Verbatim from `ReaderTemplate.cs:89-195`:

| Identifier | Return TypeSyntax | `clrTypeName` filter | `typeLabel` |
|---|---|---|---|
| GetBoolean | `PredefinedType(Token(BoolKeyword))` | `"global::System.Boolean"` | `"a Boolean"` |
| GetByte | `PredefinedType(Token(ByteKeyword))` | `"global::System.Byte"` | `"a Byte"` |
| GetChar | `PredefinedType(Token(CharKeyword))` | `"global::System.Char"` | `"a Char"` |
| GetDateTime | `ParseTypeName("global::System.DateTime")` | `"global::System.DateTime"` | `"a DateTime"` |
| GetDecimal | `PredefinedType(Token(DecimalKeyword))` | `"global::System.Decimal"` | `"a Decimal"` |
| GetDouble | `PredefinedType(Token(DoubleKeyword))` | `"global::System.Double"` | `"a Double"` |
| GetFloat | `PredefinedType(Token(FloatKeyword))` | `"global::System.Single"` | `"a Single"` |
| GetGuid | `ParseTypeName("global::System.Guid")` | `"global::System.Guid"` | `"a Guid"` |
| GetInt16 | `PredefinedType(Token(ShortKeyword))` | `"global::System.Int16"` | `"an Int16"` |
| GetInt32 | `PredefinedType(Token(IntKeyword))` | `"global::System.Int32"` | `"an Int32"` |
| GetInt64 | `PredefinedType(Token(LongKeyword))` | `"global::System.Int64"` | `"an Int64"` |
| GetString | `PredefinedType(Token(StringKeyword))` | `"global::System.String"` | `"a String"` |

The `typeLabel` reproduces the original message exactly: `$"Field {i} is not {typeLabel} column."` → `"Field {i} is not a Boolean column."` / `"Field {i} is not an Int32 column."` (note the `a`/`an` and that `float` uses the label `Single`).

---

## Task 1: Prove the composition pattern (generic, durable)

This is the empirical gate for the whole plan: a standalone child `[Template]` getter with a method-level `[Splice]` return type, invoked by a sibling `[Splice]` member-generator that renames each result with `.WithIdentifier(...)`. It exercises the one residual unknown — `Member<TRet>` where `TRet` is a `[Splice]` type param — in isolation, before touching ObjectReader.

**Files:**
- Create: `test/Synto.Test/Templating/ChildTemplateTest.cs`
- Create (via accept): `test/Synto.Test/Templating/snapshots/ChildTemplateTest.ChildTemplate_MemberGeneratorInvokesChildFactory#Factory.Reader.g.verified.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: documents the blessed pattern — a standalone `[Template]` method `TRet TypedGetter<[Splice] TRet>(...)` → `Factory.TypedGetter(TypeSyntax TRet, …)` returning `MethodDeclarationSyntax`, invoked from a `[Splice] static IEnumerable<MemberDeclarationSyntax>` generator via `Factory.TypedGetter(...).WithIdentifier(Identifier("..."))`. Tasks 2–3 rely on this exact shape compiling and generating.

- [ ] **Step 1: Write the snapshot test**

Model it on `SpliceMemberGeneratorTest.MemberGenerator_CanonicalShape` (string-source + Verify). The harness helpers (`CompilationWithSource`, the `Verify(...).UseDirectory("snapshots")` call) follow the existing `SpliceMemberGeneratorTest.cs` in the same folder — match that file's class scaffolding (metadata references, `CompilationWithSource`, usings) exactly; only the template string and test body below are new.

Create `test/Synto.Test/Templating/ChildTemplateTest.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto;
using Xunit;

namespace Synto.Test.Templating;

public class ChildTemplateTest
{
    // NOTE: copy the metadata-reference fields + CompilationWithSource(...) helper verbatim from
    // SpliceMemberGeneratorTest.cs in this same folder (identical compilation setup). Only the
    // template constant and the [Fact] below are unique to this file.

    private const string ChildGetterComposition =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

        partial class Factory {}

        public readonly record struct Col(int Ordinal, string Name, string ClrType);

        // Standalone child template: one typed getter, return type supplied per call via a [Splice] type param.
        // The inert `_e` lets the carrier compile alone; only `_e.Current` is quoted into output.
        internal sealed class GetterTemplate
        {
            private readonly System.Collections.IEnumerator _e = default!;

            [Template(typeof(Factory))]
            public TRet TypedGetter<[Splice] TRet>(int i)
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                var clrType = Parameter<string>();
                var typeLabel = Parameter<string>();
                foreach (var c in columns.Where(c => c.ClrType == clrType))
                    if (i == c.Ordinal)
                        return Member<TRet>(_e.Current, c.Name);
                throw new global::System.InvalidCastException($"Field {i} is not {typeLabel} column.");
            }
        }

        // Parent carrier: a [Splice] member-generator invokes the child factory per getter and renames each result.
        [Template(typeof(Factory))]
        public class Reader
        {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Getters()
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                yield return Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, "System.Int32", "an Int32")
                    .WithIdentifier(Identifier("GetInt32"));
                yield return Factory.TypedGetter(PredefinedType(Token(StringKeyword)), columns, "System.String", "a String")
                    .WithIdentifier(Identifier("GetString"));
            }
        }
        """;

    [Fact]
    public async Task ChildTemplate_MemberGeneratorInvokesChildFactory()
    {
        var compilation = CompilationWithSource(ChildGetterComposition);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }
}
```

- [ ] **Step 2: Run it and confirm it "fails" by writing a received snapshot (no verified yet)**

Run:
```bash
rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "ChildTemplateTest.ChildTemplate_MemberGeneratorInvokesChildFactory"
```
Expected: FAIL — Verify reports a new received snapshot because no `.verified.cs` exists yet (this is the normal first-run Verify behavior). **Decision point:** if instead the run reports a *generator diagnostic / compile error* in the generated `Factory.Reader.g.cs` or `Factory.TypedGetter.g.cs` (e.g. `Member<TRet>` not handled for a `[Splice]` type param), STOP and switch to systematic-debugging — that is the one real gap this task exists to find. Fix sites to investigate first: the `Member<>` rewriting in `TemplateFactorySourceGenerator.cs` and `StagedTypeParameterFinder.cs`. Do not proceed to Step 3 until generation is clean.

- [ ] **Step 3: Review the received snapshot for correctness, then accept it**

Open the generated `Factory.Reader.g.cs` received output. Confirm:
- `Factory.TypedGetter(TypeSyntax TRet, global::System.Collections.Generic.IReadOnlyList<global::Col> columns, string clrType, string typeLabel)` returns a `MethodDeclarationSyntax` whose return type slot is `TRet` and whose body unrolls the `.Where(...)`/`Member<TRet>` into a member access on `_e.Current`.
- The `Factory.Reader(...)` body contains a `__spliceGenerator_Getters_0()` local function that calls `Factory.TypedGetter(...).WithIdentifier(Identifier("GetInt32"))` / `("GetString")` and splices via `ListSegment<MemberDeclarationSyntax>.Run(...)`.

If correct, accept the received snapshot (rename `.received.cs` → `.verified.cs` and stage it).

- [ ] **Step 4: Re-run to confirm PASS**

Run:
```bash
rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "ChildTemplateTest.ChildTemplate_MemberGeneratorInvokesChildFactory"
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
rtk jj commit -m "test(templating): child [Template] getter invoked by [Splice] member-generator (composition proof)"
```

---

## Task 2: Add the standalone child getter template to the ObjectReader generator

Introduce `Factory.TypedGetter(...)` in the generator project. Nothing calls it yet, so this task is purely "the child template compiles and generates its factory" — no behavior change, snapshot still green.

**Files:**
- Create: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/GetterTemplate.cs`
- Test (gate): the generator project must build; existing `Synto.Example.ObjectReader.Tests` stay green.

**Interfaces:**
- Consumes: Task 1's blessed shape; `ColumnInfo` (`Model.cs:10`: `internal readonly record struct ColumnInfo(int Ordinal, string Name, string ColumnTypeName)`); `EquatableArray<>` (`Synto.Generators`); `Parameter<>`/`Member<>` (`Synto.Templating`).
- Produces: `Factory.TypedGetter(TypeSyntax TRet, EquatableArray<ColumnInfo> columns, string clrTypeName, string typeLabel)` → `MethodDeclarationSyntax`. Task 3's member-generator calls exactly this signature.

- [ ] **Step 1: Confirm the baseline is green (characterization)**

Run:
```bash
rtk dotnet test examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj
```
Expected: PASS (snapshot + behavior + incremental). This is the regression baseline for Tasks 2–3.

- [ ] **Step 2: Create the child template**

Create `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/GetterTemplate.cs`. The body mirrors a current getter (`ReaderTemplate.cs:89-96`) exactly, with the literal return type / filter / message replaced by the `[Splice] TRet` type param and three folded `Parameter<>` values:

```csharp
using System.Linq;
using Synto.Generators;
using Synto.Templating;
using static Synto.Templating.Template;

namespace Synto.Example.ObjectReader.Generator;

// Child [Template] (child-templates narrow case): ONE typed getter, specialized per CLR type. The 12 typed getters in
// ReaderTemplate.cs collapse to repeated invocations of this single template (see the TypedGetters member-generator).
// Authored standalone with an inert non-generic `_e` so the carrier compiles alone — only `_e.Current` is quoted, and
// it quotes to the same `_e.Current.<member>` access the real reader exposes (its `_e` is `IEnumerator<T>`). The return
// type is supplied per call as a `TypeSyntax` via the `[Splice] TRet` param; the caller renames via `.WithIdentifier`.
#pragma warning disable CA1812 // template carrier, only ever quoted — never instantiated.
internal sealed class GetterTemplate
{
    private readonly global::System.Collections.IEnumerator _e = default!;

    [Template(typeof(Factory))]
    public TRet TypedGetter<[Splice] TRet>(int i)
    {
        var columns = Parameter<EquatableArray<ColumnInfo>>();
        var clrTypeName = Parameter<string>();
        var typeLabel = Parameter<string>();
        foreach (var c in columns.Where(c => c.ColumnTypeName == clrTypeName))
            if (i == c.Ordinal)
                return Member<TRet>(_e.Current, c.Name);
        throw new global::System.InvalidCastException($"Field {i} is not {typeLabel} column.");
    }
}
#pragma warning restore CA1812
```

- [ ] **Step 3: Build the generator project and confirm `Factory.TypedGetter` generates**

Run:
```bash
rtk dotnet build examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Synto.Example.ObjectReader.Generator.csproj
```
Expected: BUILD SUCCEEDED, no Synto diagnostics. (The `TemplateFactorySourceGenerator` runs as an analyzer over this project and emits `Factory.TypedGetter(...)` into the `Factory` partial declared at `ReaderTemplate.cs:215`.)

- [ ] **Step 4: Confirm the example tests are still green (nothing calls the child yet)**

Run:
```bash
rtk dotnet test examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj
```
Expected: PASS, snapshot unchanged (the child factory exists but is uncalled, so consumer output is identical).

- [ ] **Step 5: Commit**

```bash
rtk jj commit -m "feat(objectreader): add standalone child TypedGetter [Template]"
```

---

## Task 3: Replace the 12 getters with the `[Splice]` member-generator

The migration proper: delete the 12 hand-repeated getter methods and emit them from one member-generator that loops the getter table and calls `Factory.TypedGetter(...)`. Gate = the existing ObjectReader snapshot stays **byte-identical** and behavior tests stay green.

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs:87-195` (delete the 12 getters; insert the member-generator at the same position so output order is preserved).
- Test (gate): `Synto.Example.ObjectReader.Tests` snapshot + behavior + incremental tests.

**Interfaces:**
- Consumes: `Factory.TypedGetter(TypeSyntax, EquatableArray<ColumnInfo>, string, string)` from Task 2.
- Produces: the final ObjectReader carrier — its 12 typed getters now emitted via composition. No new public surface.

- [ ] **Step 1: Add the SyntaxFactory usings to the carrier**

The member-generator authors syntax (`PredefinedType`, `Token`, `ParseTypeName`, `Identifier`). Add to the top of `ReaderTemplate.cs` (alongside the existing `using static Synto.Templating.Template;` at line 4):

```csharp
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
```

- [ ] **Step 2: Delete the 12 getter methods**

Remove `ReaderTemplate.cs:87-195` — the `// ---- cast-less typed getters ...` comment banner through the end of `GetString` (the entire `GetBoolean`…`GetString` run). Leave `GetValue` (ends line 85) and the `// ---- shared helpers ...` banner (line 197) and `OutOfRange`/`NoColumn` intact.

- [ ] **Step 3: Insert the member-generator in their place**

At the position the getters occupied (between `GetValue` and the shared-helpers banner), insert:

```csharp
    // ---- cast-less typed getters: ONE child [Template] (GetterTemplate.TypedGetter) invoked per CLR type ----------
    // child-templates narrow case: the 12 typed getters collapse to a single [Splice] member-generator that calls
    // Factory.TypedGetter(returnType, columns, filter, label) per getter and renames each result via .WithIdentifier.
    // Runs at factory-build time; the yielded MethodDeclarationSyntax members splice in at this position (order preserved).
    [Splice]
    static global::System.Collections.Generic.IEnumerable<MemberDeclarationSyntax> TypedGetters()
    {
        var columns = Parameter<EquatableArray<ColumnInfo>>();
        yield return Factory.TypedGetter(PredefinedType(Token(BoolKeyword)), columns, "global::System.Boolean", "a Boolean").WithIdentifier(Identifier("GetBoolean"));
        yield return Factory.TypedGetter(PredefinedType(Token(ByteKeyword)), columns, "global::System.Byte", "a Byte").WithIdentifier(Identifier("GetByte"));
        yield return Factory.TypedGetter(PredefinedType(Token(CharKeyword)), columns, "global::System.Char", "a Char").WithIdentifier(Identifier("GetChar"));
        yield return Factory.TypedGetter(ParseTypeName("global::System.DateTime"), columns, "global::System.DateTime", "a DateTime").WithIdentifier(Identifier("GetDateTime"));
        yield return Factory.TypedGetter(PredefinedType(Token(DecimalKeyword)), columns, "global::System.Decimal", "a Decimal").WithIdentifier(Identifier("GetDecimal"));
        yield return Factory.TypedGetter(PredefinedType(Token(DoubleKeyword)), columns, "global::System.Double", "a Double").WithIdentifier(Identifier("GetDouble"));
        yield return Factory.TypedGetter(PredefinedType(Token(FloatKeyword)), columns, "global::System.Single", "a Single").WithIdentifier(Identifier("GetFloat"));
        yield return Factory.TypedGetter(ParseTypeName("global::System.Guid"), columns, "global::System.Guid", "a Guid").WithIdentifier(Identifier("GetGuid"));
        yield return Factory.TypedGetter(PredefinedType(Token(ShortKeyword)), columns, "global::System.Int16", "an Int16").WithIdentifier(Identifier("GetInt16"));
        yield return Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, "global::System.Int32", "an Int32").WithIdentifier(Identifier("GetInt32"));
        yield return Factory.TypedGetter(PredefinedType(Token(LongKeyword)), columns, "global::System.Int64", "an Int64").WithIdentifier(Identifier("GetInt64"));
        yield return Factory.TypedGetter(PredefinedType(Token(StringKeyword)), columns, "global::System.String", "a String").WithIdentifier(Identifier("GetString"));
    }
```

Note: `var columns = Parameter<EquatableArray<ColumnInfo>>();` folds into the `Factory.ObjectReaderTemplate(...)` factory's existing `columns` parameter (the carrier already lifts the same value in every getter), so the member-generator captures it in scope and forwards it to each child call.

- [ ] **Step 4: Build the generator project**

Run:
```bash
rtk dotnet build examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Synto.Example.ObjectReader.Generator.csproj
```
Expected: BUILD SUCCEEDED. (Carrier compiles: `Factory.TypedGetter` exists from Task 2; `MemberDeclarationSyntax`/`Identifier`/`PredefinedType` resolve via the Step 1 usings.)

- [ ] **Step 5: Run the example tests — snapshot MUST stay byte-identical**

Run:
```bash
rtk dotnet test examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj
```
Expected: PASS with **no snapshot change** — `ObjectReaderSnapshotTests` green against the unchanged `...#ObjectReader.g.verified.cs`, `ObjectReaderBehaviorTests` green, `ObjectReaderIncrementalTests` (cacheability) green.

If a snapshot `.received.cs` appears, diff it against `.verified.cs`. The only acceptable divergence is none. Common mismatch causes to fix in the child (Task 2) rather than accepting the diff: a return-type syntax differing from the original (`ParseTypeName` vs the literal), getter ordering, or the exception message (`typeLabel` text). Iterate `GetterTemplate.cs` / the table until byte-identical. Do **not** accept a non-identical snapshot — that would mean the refactor changed behavior.

- [ ] **Step 6: Run the full templating suite (no regressions elsewhere)**

Run:
```bash
rtk dotnet test test/Synto.Test/Synto.Test.csproj
```
Expected: PASS (including Task 1's `ChildTemplateTest`).

- [ ] **Step 7: Format and commit**

```bash
rtk dotnet format whitespace examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Synto.Example.ObjectReader.Generator.csproj
rtk jj commit -m "refactor(objectreader): collapse 12 typed getters into one child [Template] via [Splice] member-generator"
```

---

## Task 4: Bless the pattern + sync docs and memory

Record that "a template invoking a template" (item #1 in the spec) is now an intentional, demonstrated pattern, and update tracking.

**Files:**
- Modify: `docs/superpowers/specs/2026-06-28-child-templates-design.md` (status line).
- Modify: `C:\Users\redoz\.claude\projects\C--dev-Synto\memory\MEMORY.md` + `child-templates-design-inflight.md` pointer.

- [ ] **Step 1: Update the spec status**

In `docs/superpowers/specs/2026-06-28-child-templates-design.md`, change the `**Status:**` line to note the narrow member-level case has shipped (ObjectReader's 12 typed getters collapsed to one child `[Template]`), with the inline-fragment graft + position-aware validation still deferred. Add a one-line pointer to this plan.

- [ ] **Step 2: Update the memory pointer**

The memory note `child-templates-design-inflight` is now stale (it predates the spec and this implementation). Update `child-templates-design-inflight.md` (and its `MEMORY.md` line) to reflect: spec written + narrow case implemented (12 getters → one child template), deferred items remaining. Keep it one fact per the memory conventions.

- [ ] **Step 3: Commit**

```bash
rtk jj commit -m "docs(spec): mark child-templates narrow case shipped; refresh memory pointer"
```

---

## Self-Review

**1. Spec coverage** (`2026-06-28-child-templates-design.md`):
- "Child template = `[Template]` with explicit `[Splice]` target type" → Tasks 1–2 (`TypedGetter<[Splice] TRet>`). ✓
- Section A "member-level composition (ships first)" — `[Splice]` member-generator invoking the child factory per item → Tasks 1 & 3. ✓
- "Member-name variance via `.WithIdentifier(...)`" (resolved decision) → `.WithIdentifier(Identifier("GetXxx"))` in Tasks 1 & 3. ✓
- "Demonstrated by migrating ObjectReader's typed getters to one child template" → Tasks 2–3 (12 getters). ✓
- Carrier-must-compile / field-visibility (resolved "decide per migration") → standalone child with inert `_e` (Task 2). ✓
- Cacheability (`TypeSyntax`/`EquatableArray`/primitives only) → Global Constraints + Task 3 Step 5 keeps `ObjectReaderIncrementalTests` green. ✓
- Deferred items (inline `Splice(Factory.X())` graft, position-aware validation) → explicitly out of scope. ✓
- Spec's "13 getters" reconciled to the actual 12 (Global Constraints). ✓

**2. Placeholder scan:** No TBD/"handle edge cases"/"similar to Task N" — every code step shows complete code; the getter table is fully enumerated. The one deliberately conditional branch (Task 1 Step 2 → systematic-debugging if generation errors) names concrete fix sites rather than hand-waving. ✓

**3. Type consistency:** `Factory.TypedGetter(TypeSyntax, EquatableArray<ColumnInfo>, string, string)` is defined identically in Task 2 Step 2 and called identically in Task 3 Step 3 (return type, then `columns`, then `clrTypeName`, then `typeLabel`). `ColumnInfo.ColumnTypeName` matches `Model.cs:10`. `TypedGetters` (the member-generator) and `TypedGetter` (the child) are distinct, consistent names. Task 1's standalone proof uses a local `Col`/`ClrType` (self-contained) and is intentionally separate from ObjectReader's `ColumnInfo`/`ColumnTypeName`. ✓
