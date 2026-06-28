# Post-Quote Declaration Decorations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a `[Template]` carrier declare its *emitted shell* — name, visibility, `sealed`, and base list — via five curated marker attributes, so Synto stops hand-fixing the quoted result with raw `SyntaxFactory` (deleting ObjectReader's `.WithIdentifier/.WithModifiers/.WithBaseList` + `RenameConstructor` glue).

**Architecture:** A scoped `DecorationFinder` walks the carrier (skipping foreign child-template subtrees via `TemplateScope`), maps each decorated declaration to an ordered list of `ApplyXxxAttribute(...)` extension-method calls, and the quoter folds those calls onto that node's base quote through a new `_postQuoteHooks` channel. The `Apply…` hooks run at the *consumer generator's* runtime (Synto only **emits** the call, never executes it) — which sidesteps the Roslyn "can't run consumer code at gen-time" wall and makes user-defined `[Foo]`+`ApplyFooAttribute` work by construction. Built-in hooks ship as emitted file-local helpers (like `ToSyntax`).

**Tech Stack:** C# / Roslyn incremental source generators (`Microsoft.CodeAnalysis.CSharp`), netstandard2.0 for the injected marker surface, xUnit + Verify for tests, MTP test runner under SDK 10.

## Global Constraints

- **Injected marker surface targets netstandard2.0** and is authored `public` in `src/Synto`, injected `internal` by `SurfaceInjectionGenerator` (auto-discovered by the `Synto.Runtime.` resource prefix — no generator code change needed to register).
- **No `ISymbol` / `SemanticModel` / `SyntaxNode` may enter cached pipeline state.** All decoration discovery and symbol resolution runs *inside* the `GenerateTemplate` per-syntax transform; only value-equatable POCOs (POCO structs, `EquatableArray<DiagnosticInfo>`) cross into the pipeline.
- **Decorations are constructor-args only** — no settable properties/fields on a decoration attribute (enforced by SY1026).
- **Decoration discovery is scoped.** It must route through `TemplateScope` / `TemplateScopedWalker` so a nested child `[Template]`'s decorations belong to the child's own factory and never leak to the parent.
- **ObjectReader stays semantically equivalent — NOT byte-identical.** The generated reader must compile and keep `ObjectReader.Tests` 10/10 green; the snapshot may be re-accepted if its diff is benign (modifier/base-list token order, trivia, an injected `Apply…` helper). Only an unreviewed behavioral change is a red flag. (See memory `generated-output-semantic-not-byte-identical`.)
- **Diagnostics are mechanical** — symbol/syntax facts only, never LLM-judged.
- **VCS:** jj repo, work on the `experimental` bookmark (never `main`). Commit with `jj commit <paths>` (do NOT `jj split` — it hangs). Advance the bookmark with `jj bookmark set experimental -r @-` after each commit. Push only if explicitly asked.
- **Tests:** MTP under SDK 10 — run a project with `dotnet test --project <csproj>`; a single test via `--filter-method "*Name*"` (NOT `--filter`). Format gate: `dotnet format whitespace --verify-no-changes` exit 0; no `*.received.cs` left behind.
- **Diagnostic ID allocation:** next free template IDs are `SY1022`–`SY1028` (catalog `src/Synto.SourceGenerator/Diagnostics.cs`; `SY12xx` is the Matching range — do not collide).

---

## File Structure

**Created — injected marker surface (`src/Synto/Templating/`, public, netstandard2.0):**
- `IdentifierAttribute.cs` — `[Identifier]`, no-arg value hole (injects a `string` factory param).
- `VisibilityAttribute.cs` — `[Visibility(Access)]`.
- `SealedAttribute.cs` — `[Sealed]`.
- `ImplementsAttribute.cs` — generic `ImplementsAttribute<TInterface>` (repeatable).
- `InheritsAttribute.cs` — generic `InheritsAttribute<TBase>` (single).
- `Access.cs` — `enum Access { Public, Internal, Private, Protected, ProtectedInternal, PrivateProtected, File }`.

**Created — built-in `Apply…` file-local helpers (`src/Synto/`, public static, emitted file-local like `ToSyntax`):**
- `IdentifierAttributeExtensions.cs` — `ApplyIdentifierAttribute<T>` (rename type **and** its constructors).
- `VisibilityAttributeExtensions.cs` — `ApplyVisibilityAttribute<T>` (map `Access`→modifier tokens).
- `SealedAttributeExtensions.cs` — `ApplySealedAttribute<T>` (idempotent add `sealed`).
- `ImplementsAttributeExtensions.cs` — `ApplyImplementsAttribute<T>` (append interface base type).
- `InheritsAttributeExtensions.cs` — `ApplyInheritsAttribute<T>` (prepend base-class base type).

**Created — generator internals (`src/Synto.SourceGenerator/Templating/`):**
- `DecorationMarkers.cs` — resolves the built-in attribute symbols (bound + unbound generic) + `Access` enum from a `Compilation` (mirrors `Matching/MatchMarkers.cs`).
- `DecorationFinder.cs` — the scoped walker + `DecorationResult` / `AppliedDecoration` / `InjectedParameter` model + validation.

**Modified — generator internals:**
- `Synto.SourceGenerator.csproj` — 6 `Synto.Runtime.*` + 5 `Synto.Helper.*` `EmbeddedResource` entries.
- `Templating/FileLocalHelpers.cs` — 5 new `HelperEntry` rows.
- `Templating/TemplateSyntaxQuoter.cs` — new `_postQuoteHooks` channel + fold in `Visit`.
- `Templating/TemplateFactorySourceGenerator.cs` — call `DecorationFinder`, inject params, add trim nodes, pass hooks to quoter.
- `Diagnostics.cs` — 7 descriptors + factory methods (`SY1022`–`SY1028`).

**Modified — ObjectReader example (migration, last task):**
- `…/Generator/ReaderTemplate.cs` — add markers to the carrier class.
- `…/Generator/ObjectReaderGenerator.cs` — delete the `.WithIdentifier/.WithModifiers/.WithBaseList` chain + `RenameConstructor`; pass the reader name as the `[Identifier]` string arg.
- `…/Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs` — re-accept benign diff.

**Created — tests (`test/Synto.Test/Templating/`):**
- `ApplyHelperTests.cs` — direct unit tests of the 5 helper behaviors.
- `DecorationTests.cs` — per-marker round-trip, composed, nested-scope, user-defined smoke.
- `DecorationDiagnosticsTests.cs` — one negative per SY1022–SY1028.

---

## Task 1: Consumer surface — 5 markers + `Access` enum

**Files:**
- Create: `src/Synto/Templating/Access.cs`, `IdentifierAttribute.cs`, `VisibilityAttribute.cs`, `SealedAttribute.cs`, `ImplementsAttribute.cs`, `InheritsAttribute.cs`
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` (resource ItemGroup, ~lines 43–72)
- Test: `test/Synto.Test/Templating/DecorationTests.cs` (new — surface-injection assertion only in this task)

**Interfaces:**
- Produces: marker types under `namespace Synto.Templating` — `IdentifierAttribute`, `VisibilityAttribute(Access access)`, `SealedAttribute`, `ImplementsAttribute<TInterface>`, `InheritsAttribute<TBase>`, and `enum Access`. Later tasks resolve these by metadata name (`typeof(ImplementsAttribute<>).FullName` ⇒ `"Synto.Templating.ImplementsAttribute`1"`).

**Contract notes (pin, don't over-specify):**
- AttributeUsage must be **permissive enough that misapplying a marker is a Synto diagnostic (SY1022), not a C# compiler error** for the kinds the negative tests exercise (e.g. `[Sealed]`/`[Implements<T>]` reachable on a method). Concretely: include `AttributeTargets.Method` alongside `Class`/`Struct` on `SealedAttribute`, `ImplementsAttribute<>`, `InheritsAttribute<>`, `VisibilityAttribute`; `IdentifierAttribute` may stay `Class | Struct`.
- `ImplementsAttribute<TInterface>` sets `AllowMultiple = true`; `InheritsAttribute<TBase>` sets `AllowMultiple = false`. Mirror the generic-attribute shape of `src/Synto/Matching/MatchAttribute.cs`.
- `VisibilityAttribute` exposes its value as a **constructor parameter only** (`public VisibilityAttribute(Access access)` + a get-only `Access Access`).

- [ ] **Step 1: Write the failing test** — `DecorationTests.cs`, assert the surface injects as `internal`.

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.SourceGenerator; // SurfaceInjectionGenerator
using Xunit;

public class DecorationTests
{
    [Fact]
    public void MarkerSurface_IsInjectedAsInternal()
    {
        // Run SurfaceInjectionGenerator over a trivial compilation and assert the five markers + enum land as internal.
        var compilation = CSharpCompilation.Create("c",
            new[] { CSharpSyntaxTree.ParseText("class _ {}") },
            Net.Sdk.References, // reuse whatever reference set the existing tests use
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new SurfaceInjectionGenerator());
        var run = driver.RunGenerators(compilation).GetRunResult();
        var all = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));

        Assert.Contains("internal enum Access", all);
        Assert.Contains("internal sealed class IdentifierAttribute", all);
        Assert.Contains("internal", all); // VisibilityAttribute / SealedAttribute
        Assert.Contains("class ImplementsAttribute<", all);
        Assert.Contains("class InheritsAttribute<", all);
    }
}
```

(If the existing tests have a dedicated surface-injection harness/reference bundle, mirror it rather than hand-rolling `references`. Check `test/Synto.Test` for an existing `SurfaceInjectionGenerator` test to copy the compilation setup.)

- [ ] **Step 2: Run the test, verify it fails** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*MarkerSurface_IsInjectedAsInternal*"`. Expected: FAIL (types don't exist / not injected).

- [ ] **Step 3: Create the six surface files.** Author each `public` under `namespace Synto.Templating`. Representative shapes:

```csharp
// Access.cs
namespace Synto.Templating;
public enum Access { Public, Internal, Private, Protected, ProtectedInternal, PrivateProtected, File }
```
```csharp
// VisibilityAttribute.cs
namespace Synto.Templating;
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Method, AllowMultiple = false)]
public sealed class VisibilityAttribute : System.Attribute
{
    public VisibilityAttribute(Access access) => Access = access;
    public Access Access { get; }
}
```
```csharp
// ImplementsAttribute.cs  (mirror Matching/MatchAttribute.cs generic shape)
namespace Synto.Templating;
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Method, AllowMultiple = true)]
public sealed class ImplementsAttribute<TInterface> : System.Attribute { }
```
`IdentifierAttribute` (no-arg, `Class | Struct`), `SealedAttribute` (no-arg), and `InheritsAttribute<TBase>` (`AllowMultiple = false`) follow the same templates.

- [ ] **Step 4: Register the resources** in `Synto.SourceGenerator.csproj`, in the `Synto.Runtime.*` ItemGroup (~lines 43–72):

```xml
<EmbeddedResource Include="..\Synto\Templating\Access.cs" LogicalName="Synto.Runtime.Access.cs" />
<EmbeddedResource Include="..\Synto\Templating\IdentifierAttribute.cs" LogicalName="Synto.Runtime.IdentifierAttribute.cs" />
<EmbeddedResource Include="..\Synto\Templating\VisibilityAttribute.cs" LogicalName="Synto.Runtime.VisibilityAttribute.cs" />
<EmbeddedResource Include="..\Synto\Templating\SealedAttribute.cs" LogicalName="Synto.Runtime.SealedAttribute.cs" />
<EmbeddedResource Include="..\Synto\Templating\ImplementsAttribute.cs" LogicalName="Synto.Runtime.ImplementsAttribute.cs" />
<EmbeddedResource Include="..\Synto\Templating\InheritsAttribute.cs" LogicalName="Synto.Runtime.InheritsAttribute.cs" />
```

- [ ] **Step 5: Run the test, verify it passes.** Same command as Step 2. Expected: PASS.

- [ ] **Step 6: Build the solution + format gate.** `dotnet build` (0 errors), `dotnet format whitespace --verify-no-changes` (exit 0).

- [ ] **Step 7: Commit.** `jj commit src/Synto/Templating/Access.cs src/Synto/Templating/IdentifierAttribute.cs src/Synto/Templating/VisibilityAttribute.cs src/Synto/Templating/SealedAttribute.cs src/Synto/Templating/ImplementsAttribute.cs src/Synto/Templating/InheritsAttribute.cs src/Synto.SourceGenerator/Synto.SourceGenerator.csproj test/Synto.Test/Templating/DecorationTests.cs -m "feat(templating): decoration marker surface (Identifier/Visibility/Sealed/Implements/Inherits + Access)"` then `jj bookmark set experimental -r @-`.

---

## Task 2: Built-in `Apply…` file-local helpers + registry

**Files:**
- Create: `src/Synto/IdentifierAttributeExtensions.cs`, `VisibilityAttributeExtensions.cs`, `SealedAttributeExtensions.cs`, `ImplementsAttributeExtensions.cs`, `InheritsAttributeExtensions.cs`
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` (`Synto.Helper.*` ItemGroup, ~lines 93–99); `src/Synto.SourceGenerator/Templating/FileLocalHelpers.cs` (`Entries`, ~lines 45–66)
- Test: `test/Synto.Test/Templating/ApplyHelperTests.cs` (new)

**Interfaces:**
- Produces (type-preserving extension methods — return type **==** `this` type, so calls compose):
  - `static T ApplyIdentifierAttribute<T>(this T node, string identifier) where T : TypeDeclarationSyntax` — renames the type identifier **and** every `ConstructorDeclarationSyntax` member to `identifier`.
  - `static T ApplyVisibilityAttribute<T>(this T node, Access access) where T : MemberDeclarationSyntax` — replaces the access modifiers (and `file`) with the tokens for `access`, preserving non-access modifiers.
  - `static T ApplySealedAttribute<T>(this T node) where T : TypeDeclarationSyntax` — adds `sealed` if absent (idempotent).
  - `static T ApplyImplementsAttribute<T>(this T node, string interfaceFqn) where T : TypeDeclarationSyntax` — appends `SimpleBaseType(ParseTypeName(interfaceFqn))` to the base list.
  - `static T ApplyInheritsAttribute<T>(this T node, string baseFqn) where T : TypeDeclarationSyntax` — inserts `SimpleBaseType(ParseTypeName(baseFqn))` as the **first** base type.
- Consumed by: the quoter's `_postQuoteHooks` channel (Task 3) which emits `<quote>.ApplyXxxAttribute(<args>)`; `FindReferencedHelpers` (`TemplateFactorySourceGenerator.cs:228`) auto-injects the matching helper when the factory text references the method name.

**Contract notes:**
- These helpers are authored `public static class …Extensions` in `src/Synto` (like `LiteralSyntaxExtensions`); `SurfaceInjectionGenerator`/`FileLocalHelpers` rewrites them to `file static class` when injected. The `Access` parameter type resolves because `Access` is injected `internal` into the same consumer compilation (Task 1).
- The `where T : …Syntax` bound IS the SY1022 target contract that `DecorationFinder` reads back in Task 3.

- [ ] **Step 1: Write the failing tests** — `ApplyHelperTests.cs`, exercising each helper directly (pure C#, no generator run):

```csharp
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Synto; // the helper classes
using Synto.Templating; // Access
using Xunit;

public class ApplyHelperTests
{
    static ClassDeclarationSyntax Parse(string src) =>
        (ClassDeclarationSyntax)CSharpSyntaxTree.ParseText(src).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().First();

    [Fact]
    public void ApplyIdentifier_RenamesTypeAndConstructors()
    {
        var c = Parse("class Foo { public Foo() {} }");
        var r = c.ApplyIdentifierAttribute("Bar");
        Assert.Equal("Bar", r.Identifier.Text);
        Assert.Equal("Bar", r.Members.OfType<ConstructorDeclarationSyntax>().Single().Identifier.Text);
    }

    [Fact]
    public void ApplyVisibility_File_ReplacesAccess()
    {
        var c = Parse("internal sealed class Foo {}");
        var r = c.ApplyVisibilityAttribute(Access.File);
        Assert.Contains("file", r.Modifiers.ToString());
        Assert.DoesNotContain("internal", r.Modifiers.ToString());
        Assert.Contains("sealed", r.Modifiers.ToString()); // non-access modifier preserved
    }

    [Fact]
    public void ApplySealed_IsIdempotent()
    {
        var once = Parse("class Foo {}").ApplySealedAttribute();
        var twice = once.ApplySealedAttribute();
        Assert.Single(twice.Modifiers, m => m.IsKind(SyntaxKind.SealedKeyword));
    }

    [Fact]
    public void ApplyInheritsThenImplements_OrdersBaseList()
    {
        var c = Parse("class Foo {}")
            .ApplyInheritsAttribute("global::B")
            .ApplyImplementsAttribute("global::I1")
            .ApplyImplementsAttribute("global::I2");
        var bases = c.BaseList!.Types.Select(t => t.ToString().Trim()).ToArray();
        Assert.Equal(new[] { "global::B", "global::I1", "global::I2" }, bases);
    }
}
```

- [ ] **Step 2: Run the tests, verify they fail** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*ApplyHelperTests*"`. Expected: FAIL (helpers don't exist).

- [ ] **Step 3: Implement the five helper files.** Representative (`IdentifierAttributeExtensions.cs`); the others follow the signatures above:

```csharp
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
namespace Synto;
public static class IdentifierAttributeExtensions
{
    public static T ApplyIdentifierAttribute<T>(this T node, string identifier) where T : TypeDeclarationSyntax
    {
        var renamed = node.WithIdentifier(Identifier(identifier));
        var members = renamed.Members;
        for (int i = 0; i < members.Count; i++)
            if (members[i] is ConstructorDeclarationSyntax ctor)
                members = members.Replace(ctor, ctor.WithIdentifier(Identifier(identifier)));
        return (T)renamed.WithMembers(members);
    }
}
```
For `ApplyVisibilityAttribute`, map `Access`→token list (`Public`→`public`, `File`→`file`, `ProtectedInternal`→`protected internal`, `PrivateProtected`→`private protected`, etc.), drop any existing access/`file` tokens, and prepend the new ones while keeping the rest (reuse the modifier-replace idiom in `FileLocalHelpers.cs:175-189` as a reference). Cast back to `T`.

- [ ] **Step 4: Register the helpers.** Add to `Synto.SourceGenerator.csproj` (`Synto.Helper.*` ItemGroup):

```xml
<EmbeddedResource Include="..\Synto\IdentifierAttributeExtensions.cs" LogicalName="Synto.Helper.IdentifierAttributeExtensions.cs" />
<EmbeddedResource Include="..\Synto\VisibilityAttributeExtensions.cs" LogicalName="Synto.Helper.VisibilityAttributeExtensions.cs" />
<EmbeddedResource Include="..\Synto\SealedAttributeExtensions.cs" LogicalName="Synto.Helper.SealedAttributeExtensions.cs" />
<EmbeddedResource Include="..\Synto\ImplementsAttributeExtensions.cs" LogicalName="Synto.Helper.ImplementsAttributeExtensions.cs" />
<EmbeddedResource Include="..\Synto\InheritsAttributeExtensions.cs" LogicalName="Synto.Helper.InheritsAttributeExtensions.cs" />
```

And add the five `HelperEntry` rows to `FileLocalHelpers.Entries` (define matching `…Resource` consts, mirror existing rows):

```csharp
new HelperEntry(nameof(IdentifierAttributeExtensions.ApplyIdentifierAttribute), Load(IdentifierResource)),
new HelperEntry(nameof(VisibilityAttributeExtensions.ApplyVisibilityAttribute), Load(VisibilityResource)),
new HelperEntry(nameof(SealedAttributeExtensions.ApplySealedAttribute), Load(SealedResource)),
new HelperEntry(nameof(ImplementsAttributeExtensions.ApplyImplementsAttribute), Load(ImplementsResource)),
new HelperEntry(nameof(InheritsAttributeExtensions.ApplyInheritsAttribute), Load(InheritsResource)),
```

- [ ] **Step 5: Run the tests, verify they pass.** Same command as Step 2. Expected: PASS (all four).

- [ ] **Step 6: Build + format gate.** `dotnet build` 0 errors; `dotnet format whitespace --verify-no-changes` exit 0.

- [ ] **Step 7: Commit.** `jj commit src/Synto/IdentifierAttributeExtensions.cs src/Synto/VisibilityAttributeExtensions.cs src/Synto/SealedAttributeExtensions.cs src/Synto/ImplementsAttributeExtensions.cs src/Synto/InheritsAttributeExtensions.cs src/Synto.SourceGenerator/Synto.SourceGenerator.csproj src/Synto.SourceGenerator/Templating/FileLocalHelpers.cs test/Synto.Test/Templating/ApplyHelperTests.cs -m "feat(templating): built-in Apply… decoration helpers (emitted file-local)"` then advance bookmark.

---

## Task 3: Quoter `_postQuoteHooks` channel

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs` (fields ~13–38, ctor ~26–39, `Visit(SyntaxNode?)` ~41–50)
- Test: `test/Synto.Test/Templating/DecorationTests.cs` (add a direct-quoter test)

**Interfaces:**
- Produces: a new optional constructor channel `IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? postQuoteHooks = null` (default empty). After a node's base quote `q` is produced, if the node is keyed, fold each hook in order: `q = InvocationExpression(MemberAccessExpression(q, IdentifierName(hook.HelperMethodName))).WithArgumentList(ArgumentList(SeparatedList(hook.Arguments.Select(Argument))))`.
- Consumes: `AppliedDecoration` (defined in Task 4's `DecorationFinder.cs`; for this task, define the struct first in `DecorationFinder.cs` as a standalone type so the quoter can reference it — see note).

**Contract notes:**
- The fold happens **after** `base.Visit(node)` and **after** the `_unquotedReplacements`/`_trimNodes` short-circuits (those still win — a replaced or trimmed node never reaches the hook). Mirror the channel-field + null-coalescing-to-empty pattern already used for `_memberSegments`/`_stringStagedRoots`.
- Define `AppliedDecoration` now (in a new `DecorationFinder.cs` holding only the model structs) so both the quoter (this task) and the finder (Task 4) compile against one definition:

```csharp
internal readonly struct AppliedDecoration
{
    public AppliedDecoration(string helperMethodName, ImmutableArray<ExpressionSyntax> arguments)
        => (HelperMethodName, Arguments) = (helperMethodName, arguments);
    public string HelperMethodName { get; }
    public ImmutableArray<ExpressionSyntax> Arguments { get; }
}
```

- [ ] **Step 1: Write the failing test** — direct quoter test in `DecorationTests.cs`. Build a tiny semantic model, construct the quoter with a one-entry `postQuoteHooks`, quote a class node, assert the emitted expression text contains the folded call.

```csharp
[Fact]
public void Quoter_FoldsPostQuoteHook_OntoNodeQuote()
{
    var tree = CSharpSyntaxTree.ParseText("class Foo {}");
    var compilation = CSharpCompilation.Create("c", new[] { tree }, /* refs as other tests use */ null);
    var model = compilation.GetSemanticModel(tree);
    var node = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First();

    var hooks = new Dictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>
    {
        [node] = ImmutableArray.Create(new AppliedDecoration(
            "ApplySealedAttribute", ImmutableArray<ExpressionSyntax>.Empty)),
    };
    var quoter = new TemplateSyntaxQuoter(model,
        new Dictionary<SyntaxNode, ExpressionSyntax>(), new HashSet<SyntaxNode>(),
        includeTrivia: false, postQuoteHooks: hooks);

    var expr = quoter.Visit(node);
    Assert.Contains("ApplySealedAttribute()", expr!.ToString());
}
```

(If `TemplateSyntaxQuoter`'s ctor signature differs, adapt — the new `postQuoteHooks` parameter is **last/optional** so existing call sites are unaffected. Confirm the existing four call sites in `StagedRegionEmitter.cs` and `TemplateFactorySourceGenerator.cs` still compile.)

- [ ] **Step 2: Run the test, verify it fails** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*Quoter_FoldsPostQuoteHook*"`. Expected: FAIL (no `postQuoteHooks` param).

- [ ] **Step 3: Add the channel.** Add the `_postQuoteHooks` field, the optional ctor param (null-coalesce to empty dict), and the fold in `Visit(SyntaxNode?)` after `base.Visit`:

```csharp
var result = base.Visit(node);
if (node is not null && result is not null && _postQuoteHooks.TryGetValue(node, out var hooks))
    foreach (var hook in hooks)
        result = InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, result, IdentifierName(hook.HelperMethodName)))
            .WithArgumentList(ArgumentList(SeparatedList(hook.Arguments.Select(Argument))));
return result;
```

- [ ] **Step 4: Run the test, verify it passes.** Same command as Step 2. Expected: PASS.

- [ ] **Step 5: Run the full `Synto.Test` suite** to confirm no regression from the quoter change — `dotnet test --project test/Synto.Test/Synto.Test.csproj`. Expected: all green (the channel is inert when unused).

- [ ] **Step 6: Build + format gate.** 0 errors; format exit 0.

- [ ] **Step 7: Commit.** `jj commit src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs src/Synto.SourceGenerator/Templating/DecorationFinder.cs test/Synto.Test/Templating/DecorationTests.cs -m "feat(templating): post-quote hook channel on the quoter"` then advance bookmark.

---

## Task 4: `DecorationFinder` + wire into the factory generator (happy path)

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/DecorationMarkers.cs`
- Modify: `src/Synto.SourceGenerator/Templating/DecorationFinder.cs` (add the finder + `DecorationResult`/`InjectedParameter` to the file that already holds `AppliedDecoration`)
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (call site after `SpliceCallFinder` ~line 1041; param assembly ~before 1116; `trimNodes` ~line 354+; quoter ctor ~1085)
- Test: `test/Synto.Test/Templating/DecorationTests.cs` (per-marker round-trip + composed)

**Interfaces:**
- `DecorationMarkers.Resolve(Compilation) -> DecorationMarkers?` — holds the resolved `INamedTypeSymbol`s for the 5 built-in attributes (bound + unbound generic for `Implements`/`Inherits`) and the `Access` enum. Returns null if the surface isn't referenced (no decorations possible). Mirror `Matching/MatchMarkers.cs:63-66` + the generic unbound-type pattern.
- `DecorationFinder.FindDecorations(SemanticModel model, SyntaxNode carrier, TemplateScope scope, DecorationMarkers markers, ISet<string> existingParamNames, List<DiagnosticInfo> diagnostics) -> DecorationResult`
  - Walks `carrier` as a `TemplateScopedWalker` (skips foreign child subtrees).
  - For each decorated declaration, builds the ordered `ImmutableArray<AppliedDecoration>` (order: `Identifier`, `Visibility`, `Sealed`, `Inherits`, then `Implements` in source order).
  - For `[Identifier]`: allocates a `string` param name (start `"identifier"`, uniquify against `existingParamNames` by appending `_`), records an `InjectedParameter(name, string-type)`, and uses `IdentifierName(name)` as the `ApplyIdentifierAttribute` arg.
  - For `[Visibility(Access.X)]`: reads the ctor arg, emits `MemberAccessExpression(global::Synto.Templating.Access, X)` (the `X` member name from the enum field) as the `ApplyVisibilityAttribute` arg.
  - For `[Implements<T>]`/`[Inherits<T>]`: resolves `T`'s fully-qualified `global::` name (mirror `Matching/MatchInfo.cs:63-66` type-arg extraction) and emits it as a `string` literal arg.
  - Collects every decoration `AttributeSyntax` node into `TrimAttributes`.
  - (Validation/diagnostics land in Task 5 — this task is the happy path; pass an empty `diagnostics` list.)

```csharp
internal readonly struct DecorationResult
{
    public IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>> Hooks { get; }
    public IReadOnlyList<AttributeSyntax> TrimAttributes { get; }
    public IReadOnlyList<InjectedParameter> InjectedParameters { get; }
}
internal readonly struct InjectedParameter
{
    public InjectedParameter(string name, TypeSyntax type) => (Name, Type) = (name, type);
    public string Name { get; }
    public TypeSyntax Type { get; }
}
```

**Wiring contract (TemplateFactorySourceGenerator):**
- After `SpliceCallFinder` (~line 1041), resolve markers and call the finder. `scope` is already built (~line 431).
- Append each `InjectedParameter` to the `parameters` list (the same list assembled ~lines 446–949) **after** the existing holes, preserving the existing `paramNames` uniqueness set.
- Add `result.TrimAttributes` to `trimNodes` (~line 354 set) so the markers are stripped from the quote.
- Pass `result.Hooks` to the final quoter as `postQuoteHooks:` (~line 1085).
- Skip cleanly when `DecorationMarkers.Resolve` returns null (no decoration surface referenced) — zero behavior change for existing templates.

- [ ] **Step 1: Write the failing round-trip tests** — `DecorationTests.cs`. Feed a carrier using each marker, run the **TemplateFactorySourceGenerator**, and assert the generated **factory** source (not consumer output). Use the existing `RunAndGetGeneratedSource`/driver harness pattern from `SimpleTemplateTest.cs` (mirror `CompilationWithSource` + `_driver`).

```csharp
[Fact]
public void Identifier_InjectsStringParam_AndEmitsApplyCall()
{
    var src = """
        using Synto.Templating;
        partial class Factory {}
        public class Holder {
            [Template(typeof(Factory))]
            [Identifier]
            class Shell { public Shell() {} }
        }
        """;
    var generated = RunFactoryAndGetSource(src); // helper: returns the generated Factory.*.g.cs text
    Assert.Contains("ApplyIdentifierAttribute(identifier)", generated);
    Assert.Contains("string identifier", generated); // injected factory parameter
}

[Fact]
public void Sealed_EmitsApplySealedCall() { /* [Sealed] on Shell -> ".ApplySealedAttribute()" */ }

[Fact]
public void Visibility_EmitsAccessArg() { /* [Visibility(Access.File)] -> "ApplyVisibilityAttribute(global::Synto.Templating.Access.File)" */ }

[Fact]
public void Implements_EmitsInterfaceFqnArg() { /* [Implements<global::System.IDisposable>] -> "ApplyImplementsAttribute(\"global::System.IDisposable\")" */ }

[Fact]
public void InheritsThenImplements_EmittedInBaseListOrder() { /* assert ApplyInheritsAttribute precedes ApplyImplementsAttribute in the emitted call chain */ }

[Fact]
public void AllMarkersComposed_ChainAllApplyCalls() { /* one Shell with all five -> all five Apply calls present */ }
```

Add the `RunFactoryAndGetSource` helper (mirror the existing generated-source assertions in `SimpleTemplateTest.cs`; the `[Template]` factory file is the generated tree whose hint name matches the factory).

- [ ] **Step 2: Run the tests, verify they fail** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*Decoration*"` (the new round-trip ones). Expected: FAIL (no `.ApplyXxxAttribute` emitted).

- [ ] **Step 3: Implement `DecorationMarkers.cs`** (resolve the 5 attrs + `Access`, bound + unbound generic), then **`DecorationFinder.FindDecorations`** (the scoped walker + model construction described in Interfaces). No diagnostics yet.

- [ ] **Step 4: Wire into `TemplateFactorySourceGenerator`** per the Wiring contract above (call site, param append, trim add, quoter `postQuoteHooks:`).

- [ ] **Step 5: Run the tests, verify they pass.** Same command as Step 2. Expected: PASS (all per-marker + composed).

- [ ] **Step 6: Run full `Synto.Test`** — `dotnet test --project test/Synto.Test/Synto.Test.csproj`. Expected: all green (existing templates unaffected). Confirm the cacheability tests (`CacheabilityAssert.AllStepsCachedOrUnchanged`) still pass — no symbol/syntax leaked into pipeline state.

- [ ] **Step 7: Build + format gate.** 0 errors; format exit 0.

- [ ] **Step 8: Commit.** `jj commit src/Synto.SourceGenerator/Templating/DecorationMarkers.cs src/Synto.SourceGenerator/Templating/DecorationFinder.cs src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs test/Synto.Test/Templating/DecorationTests.cs -m "feat(templating): DecorationFinder wires markers into factory via post-quote hooks"` then advance bookmark.

---

## Task 5: Diagnostics (SY1022–SY1028)

**Files:**
- Modify: `src/Synto.SourceGenerator/Diagnostics.cs` (descriptor catalog + factory methods)
- Modify: `src/Synto.SourceGenerator/Templating/DecorationFinder.cs` (validation → accumulate `DiagnosticInfo`, skip the offending decoration)
- Test: `test/Synto.Test/Templating/DecorationDiagnosticsTests.cs` (new — one negative per ID)

**Interfaces:**
- Consumes: the `List<DiagnosticInfo> diagnostics` already threaded into `FindDecorations` (Task 4). When a decoration fails validation, add the diagnostic and **drop that decoration** from the hook list (the rest of the template still generates — matches the existing `ValidateTemplate` accumulate-and-continue pattern; a fatal error leaves the template producing no factory only if it can't proceed).
- Produces: 7 `DiagnosticDescriptor`s + matching factory methods on `Diagnostics` (mirror `Diagnostics.cs:29-34` shape and the `DiagnosticInfo`-returning factories). Emission is automatic via `Emit` (`TemplateFactorySourceGenerator.cs:74`).

**Diagnostic contracts (mechanical — symbol/syntax facts only):**
| ID | Fires when |
|---|---|
| SY1022 | A marker is applied to a declaration whose node type is **not assignable** to the resolved hook's `this`-parameter type (e.g. `[Implements<T>]` on a method; `[Identifier]` on a node with no renameable identifier). For built-ins the `this`-type is the helper's `where T : …Syntax` bound. |
| SY1023 | `[Visibility(Access.File)]` on a **non-top-level** declaration (`file` is top-level only). |
| SY1024 | `[Implements<T>]` where `T` is not an interface; `[Inherits<T>]` where `T` is not a non-sealed class. |
| SY1025 | Conflicting/duplicate decorations on one node: `>1 [Visibility]`, `>1 [Inherits]`, or `[Sealed]` on a non-type. |
| SY1026 | A decoration attribute type declares a **settable property or field** (must be constructor-parameters only). |
| SY1027 | No `ApplyXxxAttribute` extension method resolvable for a `[Xxx]` marker, **or** its parameter arity ≠ the attribute's constructor-arg count. (User-defined path; built-ins are always resolvable.) |
| SY1028 | A resolvable `ApplyXxxAttribute`'s **return type ≠ its `this`-parameter type** (breaks composition). |

**Contract notes:**
- SY1022/SY1028 require reading the hook's `this`-type and return type. For **built-in** markers, these are known from the helper signatures (Task 2) — hard-wire the target `this`-type per built-in (e.g. `TypeDeclarationSyntax` for `[Sealed]`/`[Implements]`/`[Inherits]`/`[Identifier]`, `MemberDeclarationSyntax` for `[Visibility]`). For **user-defined** markers, resolve the `ApplyFooAttribute` symbol via `model` extension-method lookup and read its signature.
- SY1026 inspects the **attribute type symbol's** members for settable properties/fields.

- [ ] **Step 1: Write the failing negative tests** — `DecorationDiagnosticsTests.cs`, mirroring `SimpleTemplateTest.cs:572-594` (`RunAndGetDiagnostics` + `Assert.Single(d => d.Id == "SY10xx")` + `AssertHasRealSpan`). One per ID:

```csharp
[Fact] public void ImplementsOnMethod_ReportsSY1022() { /* [Implements<global::System.IDisposable>] on a [Template] method */ }
[Fact] public void VisibilityFileOnNested_ReportsSY1023() { /* [Visibility(Access.File)] on a nested class */ }
[Fact] public void ImplementsNonInterface_ReportsSY1024() { /* [Implements<global::System.String>] */ }
[Fact] public void DuplicateVisibility_ReportsSY1025() { /* two [Visibility] on one node */ }
[Fact] public void SettablePropOnUserAttr_ReportsSY1026() { /* user [Foo] with `public string Bar { get; set; }` */ }
[Fact] public void MissingApplyForUserAttr_ReportsSY1027() { /* user [Foo] with no ApplyFooAttribute in scope */ }
[Fact] public void ApplyWrongReturn_ReportsSY1028() { /* user ApplyFooAttribute returning object */ }
```

- [ ] **Step 2: Run the tests, verify they fail** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*ReportsSY102*"`. Expected: FAIL (no such diagnostics yet).

- [ ] **Step 3: Add the 7 descriptors + factory methods** to `Diagnostics.cs` (IDs `SY1022`–`SY1028`, category `Synto.Usage`, severity `Error`, mirror existing descriptor + factory shape).

- [ ] **Step 4: Add validation in `DecorationFinder`** — emit the matching `DiagnosticInfo` and drop the offending decoration per the contracts table.

- [ ] **Step 5: Run the tests, verify they pass.** Same command as Step 2. Expected: PASS (all 7).

- [ ] **Step 6: Run full `Synto.Test`** + build + format gate. All green; 0 errors; format exit 0.

- [ ] **Step 7: Commit.** `jj commit src/Synto.SourceGenerator/Diagnostics.cs src/Synto.SourceGenerator/Templating/DecorationFinder.cs test/Synto.Test/Templating/DecorationDiagnosticsTests.cs -m "feat(templating): decoration diagnostics SY1022-SY1028"` then advance bookmark.

---

## Task 6: Scope isolation + user-defined extensibility

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/DecorationFinder.cs` (generic arg emission for user-defined attributes, if not already covered)
- Test: `test/Synto.Test/Templating/DecorationTests.cs` (add two tests)

**Interfaces:**
- Consumes: `TemplateScope` (already passed into `FindDecorations`). The nested-child test proves a decoration on a child `[Template]` does **not** appear in the parent's factory.
- Produces: the user-defined path — for a non-built-in `[Foo]` marker, resolve `ApplyFooAttribute` by convention and emit its ctor args generically (support at least `string` literal args, which the smoke test uses).

**Contract notes:**
- This is the proof the design is open by construction, per spec §2/§8 — **one** smoke test, not advertised, no extra docs.

- [ ] **Step 1: Write the failing tests**:

```csharp
[Fact]
public void DecorationOnNestedChild_DoesNotLeakToParentFactory()
{
    // Parent [Template] carrier with a nested child [Template] method carrying [Sealed]-style decoration on the child.
    // Assert the PARENT factory's source contains no Apply call for the child's decoration.
    var generated = RunFactoryAndGetSource(/* parent+child carrier */);
    Assert.DoesNotContain("ApplySealedAttribute", generated); // belongs to the child's own factory
}

[Fact]
public void UserDefinedDecoration_FlowsThroughSamePath()
{
    var src = """
        using Synto.Templating;
        public sealed class FooAttribute : System.Attribute { public FooAttribute(string tag) {} }
        public static class FooExt {
            public static T ApplyFooAttribute<T>(this T node, string tag)
                where T : Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax => node;
        }
        partial class Factory {}
        public class Holder {
            [Template(typeof(Factory))]
            [Foo("hi")]
            class Shell {}
        }
        """;
    var generated = RunFactoryAndGetSource(src);
    Assert.Contains("ApplyFooAttribute(\"hi\")", generated);
}
```

(Adjust the user-defined test's references so `TypeDeclarationSyntax` resolves in the consumer compilation — the test compilation already references `Microsoft.CodeAnalysis.CSharp`. If that reference isn't available in the harness, have the smoke test's `ApplyFooAttribute` constrain on a simpler shared base the harness exposes, keeping the convention-resolution path identical.)

- [ ] **Step 2: Run, verify fail** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*NestedChild_DoesNotLeak*" --filter-method "*UserDefinedDecoration*"`. Expected: FAIL.

- [ ] **Step 3: Implement generic user-defined arg emission** in `DecorationFinder` (if Task 4/5 didn't already): for a non-built-in marker, build `AppliedDecoration` args from the attribute's `ConstructorArguments` (string/primitive/enum literals). Confirm the scoped walker already excludes child decorations (it should, via `TemplateScopedWalker`).

- [ ] **Step 4: Run, verify pass.** Both green.

- [ ] **Step 5: Full `Synto.Test`** + build + format gate. All green.

- [ ] **Step 6: Commit.** `jj commit src/Synto.SourceGenerator/Templating/DecorationFinder.cs test/Synto.Test/Templating/DecorationTests.cs -m "feat(templating): user-defined decorations + nested-scope isolation"` then advance bookmark.

---

## Task 7: Migrate ObjectReader onto the markers

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` (carrier class, ~lines 26–33)
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ObjectReaderGenerator.cs` (`BuildReader` ~285–306, delete `RenameConstructor` ~309–312)
- Modify (re-accept): `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs`
- Test: existing `ObjectReader.Tests` (10 tests) — no new tests; this is the dog-food proof.

**Interfaces:**
- Consumes: all five markers + the injected `[Identifier]` string param. `Factory.ObjectReaderTemplate` gains a trailing `string` parameter (the reader name).

**Contract notes:**
- Carrier becomes (drop the `sealed` keyword so `[Sealed]` exercises; keep `internal` only if also adding `[Visibility(Access.File)]` — the emitted type must end up `file sealed … : IDataReader`):

```csharp
[Template(typeof(Factory))]
[Identifier]
[Visibility(Access.File)]
[Sealed]
[Implements<global::System.Data.IDataReader>]
internal class ObjectReaderTemplate<[Splice] T>
{
    // ... unchanged body, including the nested [Template] TypedGetter child and [Splice] TypedGetters() ...
}
```
- `BuildReader` collapses to (deleting the `.WithIdentifier/.WithModifiers/.WithBaseList` chain, the `RenameConstructor` projection, and the `RenameConstructor` method itself):

```csharp
private static MemberDeclarationSyntax BuildReader(ObjectReaderModel model, int index)
{
    string reader = $"ObjectReader_{model.TargetTypeShortName}_{index}";
    TypeSyntax elementType = SyntaxFactory.ParseTypeName(model.TargetTypeQualifiedName);
    return Factory.ObjectReaderTemplate(elementType, model.Columns, reader);
}
```
- The 12 typed-getter `.WithIdentifier(Identifier("GetXxx"))` calls in `TypedGetters()` **stay** (collapsing them needs the deferred format-form `[Identifier]` — out of scope, see spec §8).
- **Expected semantic-equivalence outcome:** the generated reader stays `file sealed class ObjectReader_Person_0 : global::System.Data.IDataReader` with the ctor renamed to `ObjectReader_Person_0` and `FieldCount => 2`. Token order of `file sealed` (or any benign trivia shift) may differ — that's acceptable per the Global Constraints. Behavior (the 10 tests) must not change.

- [ ] **Step 1: Run the ObjectReader tests first (baseline green)** — `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`. Expected: 10/10 PASS (pre-change baseline).

- [ ] **Step 2: Add the markers** to the `ObjectReaderTemplate` carrier (per the Contract notes). Build the generator project. Expected: 0 errors (markers inert on the carrier; carrier still does NOT declare `: IDataReader`).

- [ ] **Step 3: Collapse `BuildReader` + delete `RenameConstructor`** per the Contract notes. Build. Expected: 0 errors.

- [ ] **Step 4: Run the ObjectReader tests.** Same command as Step 1. The snapshot test may FAIL with a `.received.cs` (benign diff) — that is expected. The 4 behavior tests + 2 diagnostics + API + incremental must PASS.

- [ ] **Step 5: Review the snapshot diff, then accept if benign.** Diff the `.received.cs` against the `.verified.cs`. Confirm: same type name, same `file sealed`, same `: global::System.Data.IDataReader`, same renamed ctor, same 12 getters/members — only benign differences (token order/trivia). If benign, accept (rename `.received.cs` → `.verified.cs`, or run the project's Verify-accept). If the diff shows a **behavioral** change (missing base type, wrong name, dropped member), STOP and fix forward — do not accept.

- [ ] **Step 6: Re-run the ObjectReader tests.** Same command as Step 1. Expected: 10/10 PASS, no `.received.cs` remaining.

- [ ] **Step 7: Run full `Synto.Test` + `Synto.Diagnostics.Test`** to confirm no cross-regression — `dotnet test --project test/Synto.Test/Synto.Test.csproj` and `dotnet test --project test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj`. Expected: Synto.Test green; Diagnostics 8/8.

- [ ] **Step 8: Build solution + format gate.** `dotnet build` 0 errors; `dotnet format whitespace --verify-no-changes` exit 0; no `*.received.cs` anywhere.

- [ ] **Step 9: Commit.** `jj commit examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ObjectReaderGenerator.cs "examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs" -m "refactor(objectreader): author shell via decoration markers; drop WithIdentifier/Modifiers/BaseList + RenameConstructor"` then advance bookmark.

---

## Self-Review

**Spec coverage** (spec §-by-§ → task):
- §3.1 `[Identifier]` (value hole, type+ctor rename) → Task 1 (surface), Task 2 (`ApplyIdentifierAttribute` renames ctors), Task 4 (param injection).
- §3.2 `[Visibility(Access)]` + `file` → Task 1, Task 2 (`Access`→tokens), Task 5 (SY1023 top-level rule).
- §3.3 `[Sealed]` (idempotent) → Task 1, Task 2.
- §3.4 `[Implements<T>]` (repeatable) / `[Inherits<T>]` (single, first) → Task 1 (generic attrs), Task 2 (base-list order), Task 4 (FQN resolution + order).
- §3.5 carry-through → Task 4 (only governed axes get hooks; rest quoted as authored) + Task 7 (carrier keeps `internal`/body as authored).
- §2 `Apply…`-in-factory mechanism, type-preserving, user-defined by construction → Task 2 (helpers), Task 3 (channel), Task 4 (emit), Task 6 (user-defined).
- §4 mechanism (DecorationFinder, param assembly, hook emission, trim, file-local helpers) → Tasks 3–4 + Task 2.
- §5 diagnostics SY-D1/5/6/7/8/9/10 → Task 5 (SY1022–SY1028).
- §6 ObjectReader collapse, semantic equivalence → Task 7.
- §7 testing (per-marker, per-diagnostic, composed, nested-scope, user-defined smoke) → Tasks 4/5/6.
- §8 deferred (format-form `[Identifier]`, typed-getter collapse, IDecorationHook framework) → explicitly out of scope; noted in Task 7.

**Placeholder scan:** the per-marker test bodies in Tasks 4–6 marked `/* ... */` are deliberate shorthand for near-identical variants of the fully-shown first test in each block — the engineer copies the shown harness call and swaps the marker/assertion string. The contract (exact emitted-call substring to assert) is given for each. No `TODO`/`handle edge cases`/unshown-code-step remains.

**Type consistency:** `AppliedDecoration { HelperMethodName, Arguments }`, `DecorationResult { Hooks, TrimAttributes, InjectedParameters }`, `InjectedParameter { Name, Type }`, `DecorationFinder.FindDecorations(...)`, `DecorationMarkers.Resolve(...)`, and the quoter `postQuoteHooks:` channel name are used identically across Tasks 3–6. Helper method names (`ApplyIdentifierAttribute`/`ApplyVisibilityAttribute`/`ApplySealedAttribute`/`ApplyImplementsAttribute`/`ApplyInheritsAttribute`) match between Task 2 definitions, Task 4 emission assertions, and Task 7's expected output.
