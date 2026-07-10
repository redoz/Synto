# Quote-time Semantic Type Qualification — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Synto template output self-contained by construction — the quoter emits every type/namespace-bound simple name as its `global::`-rooted fully-qualified form, so generated code compiles regardless of the hosting document's using context.

**Architecture:** A new `QualificationFinder` (SemanticModel pass, runs inside the `[Template]` transform alongside `FacadeCallFinder`/`DecorationFinder`) records, per type-position name node, the `global::`-rooted substitute `NameSyntax`. `PlanAssembler` folds each into the existing `nodeSteps` map as a terminal `QualifyStep` middleware that re-quotes the substitute. The quoter itself stays SemanticModel-free — substitutes are plain syntax values, so the incremental-cacheability invariant holds.

**Tech Stack:** C# / Roslyn incremental source generators (netstandard2.0), xUnit v3 + Verify snapshots under Microsoft Testing Platform (MTP), jj VCS.

**Spec:** `docs/superpowers/specs/2026-07-10-quote-time-type-qualification-design.md`

## Deviation from spec §6 (M12)

The spec assumed we would share/fix the hand-rolled `SymbolExtensions.GetQualifiedNameSyntax` (M12: drops type args on generic/nested types). The architecture map showed the **decoration path already qualifies correctly** via `symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` — which renders generics, nested containing types, and the `global::` root correctly — and that `GetQualifiedNameSyntax` in `src/Synto.Core/Symbols/SymbolExtensions.cs` is **dead (no callers)**. This plan therefore builds the qualification primitive on `FullyQualifiedFormat` (mirroring decorations) and does **not** fix or adopt the dead helper. M12 is downgraded to an optional dead-code cleanup (Task 7). Spec §6's intent — "generic/nested types render correctly" — is satisfied by `FullyQualifiedFormat`.

## Deviation: `QualifyStep` COMPOSES (preserves nested splices) — revised after the gen-1 escalation

The first Task 3 attempt had `QualifyStep` re-quote the WHOLE baked substitute (`ctx.Quote(_qualified)`) and
short-circuit descent. That is defective: a `[Splice]` type argument nested inside a qualified generic name loses
its per-node `ReplaceStep`. Concretely the ObjectReader carrier is `ObjectReaderTemplate<[Splice] T>`;
`IEnumerator<T>` splices `T` to the consumer's concrete element type via a node-keyed `ReplaceStep` on the inner
`T` node (snapshot: `IEnumerator<global::Person>`). Qualifying the whole `IEnumerator<T>` and re-quoting the baked
substitute drops that splice, leaving a literal `T` in the generated reader (CS0246) and breaking the ObjectReader
dog-food.

Fix (this revision): `QualifyStep` no longer re-quotes the substitute wholesale. It re-quotes only the authored
FINAL SIMPLE NAME through the pipeline continuation `next(...)` — so any nested per-node steps (the inner `T`
splice) still fire — then re-parents that quote under the substitute's HOLES-FREE qualifier prefix. This is sound
because `next` is the continuation PAST `QualifyStep` (no self-recursion) and `base.Visit` descends into children
via the overridden `Visit`, re-dispatching their steps (`PerNodePipelineQuoter.cs:44-60`). **Tasks 1 and 2 land
unchanged** (the finder still records the full `global::`-rooted substitute via `QualifiedNameFactory.ForSymbol`);
ONLY `QualifyStep` (Task 3, Step 3a) carries the compose logic. Cacheability is preserved — the step holds only a
`NameSyntax` and produces syntax via `ctx.Quote`/`next`.

## Global Constraints

- **Workspace:** all work happens in the jj workspace at `.claude/workspaces/quote-time-type-qualification` (branch `experimental/quote-time-type-qualification`, child of `main`). Never touch `main` or the `experimental/switch-expression-getters` change directly.
- **Cacheability invariant (non-negotiable):** no `SemanticModel`, `ISymbol`, or `Compilation` may be captured into anything that flows into incremental pipeline state. The finder runs inside the transform and emits only syntax values; the quoter steps hold only `NameSyntax`.
- **LangVersion:** `preview` (repo `Directory.Build.props`); generators target `netstandard2.0`.
- **Test runner (MTP):** run a single project with `dotnet test --project <csproj> -c Debug`; filter with `--filter-method "*Name*"` / `--filter-class "*Class*"` (NOT `--filter`).
- **Snapshot acceptance:** Verify writes `*.received.cs`; accept by moving `received`→`verified`. Re-accepts are legitimate here (bare→`global::`); every re-accept MUST still post-generation-compile clean.
- **Qualification predicate (applies to every task that reasons about scope):** qualify a name node iff `GetSymbolInfo(node).Symbol` is an `INamedTypeSymbol` reached as a type/namespace reference. **Skip:** type parameters (`ITypeParameterSymbol`), `var` (`IdentifierNameSyntax.IsVar`), builder facade names / methods / locals / fields / properties (non-type symbols), names already `global::`-rooted (parent is `AliasQualifiedNameSyntax`), and the Synto surface (`Synto` / `Synto.*`). Predefined keywords (`int`, `string`) are `PredefinedTypeSyntax`, not name nodes, so they are never visited.
- **Commit mechanics:** each task ends with a jj commit in the workspace (`jj commit -m "<msg>"`). Commit messages use Conventional Commits; end with the co-author trailer only if the repo convention requires (it does not for local work here).

---

### Task 1: `QualifiedNameFactory` — symbol → `global::` NameSyntax

**Files:**
- Create: `src/Synto.Generators/Templating/Quoting/QualifiedNameFactory.cs`
- Test: `test/Synto.Test/Templating/QualifiedNameFactoryTest.cs`

**Interfaces:**
- Produces: `internal static class QualifiedNameFactory { public static NameSyntax ForSymbol(ISymbol symbol); }` — returns the `global::`-rooted fully-qualified name for a named-type or namespace symbol, preserving generic type arguments and nested containing types (type parameters render by name).

- [ ] **Step 1: Write the failing test**

```csharp
// test/Synto.Test/Templating/QualifiedNameFactoryTest.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.Generators.Templating.Quoting;

namespace Synto.Test.Templating;

public class QualifiedNameFactoryTest
{
    private static INamedTypeSymbol Lookup(string metadataName)
    {
        var compilation = CSharpCompilation.Create(
            "Q",
            references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        return compilation.GetTypeByMetadataName(metadataName)!;
    }

    [Fact]
    public void PlainType_IsGlobalRooted()
    {
        var name = QualifiedNameFactory.ForSymbol(Lookup("System.DBNull"));
        Assert.Equal("global::System.DBNull", name.ToString());
    }

    [Fact]
    public void OpenGenericType_KeepsArityAndTypeParameters()
    {
        // System.Collections.Generic.IEnumerable`1 -> global::System.Collections.Generic.IEnumerable<T>
        var name = QualifiedNameFactory.ForSymbol(Lookup("System.Collections.Generic.IEnumerable`1"));
        Assert.Equal("global::System.Collections.Generic.IEnumerable<T>", name.ToString());
    }

    [Fact]
    public void NestedType_KeepsContainingType()
    {
        // System.Collections.Generic.List`1+Enumerator -> global::System.Collections.Generic.List<T>.Enumerator
        var name = QualifiedNameFactory.ForSymbol(Lookup("System.Collections.Generic.List`1+Enumerator"));
        Assert.Equal("global::System.Collections.Generic.List<T>.Enumerator", name.ToString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class "*QualifiedNameFactoryTest*"`
Expected: FAIL — `QualifiedNameFactory` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

```csharp
// src/Synto.Generators/Templating/Quoting/QualifiedNameFactory.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating.Quoting;

/// <summary>
/// Renders a named-type or namespace symbol as its <c>global::</c>-rooted fully-qualified <see cref="NameSyntax"/>,
/// preserving generic type arguments and nested containing types (type parameters render by their bare name). Built
/// on <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> — the same primitive the decoration path uses to
/// qualify <c>[Implements&lt;T&gt;]</c> arguments — so generics and nested types render correctly by construction.
/// Runs inside the generator transform; returns a plain syntax value (no symbol/model captured).
/// </summary>
internal static class QualifiedNameFactory
{
    public static NameSyntax ForSymbol(ISymbol symbol)
        => (NameSyntax)SyntaxFactory.ParseTypeName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class "*QualifiedNameFactoryTest*"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(templating): QualifiedNameFactory — symbol to global:: NameSyntax"
```

---

### Task 2: `QualificationFinder` — record per-node `global::` substitutes

**Files:**
- Create: `src/Synto.Generators/Templating/Quoting/QualificationFinder.cs`
- Test: `test/Synto.Test/Templating/QualificationFinderTest.cs`

**Interfaces:**
- Consumes: `QualifiedNameFactory.ForSymbol` (Task 1); `TemplateScopedWalker` / `TemplateScope` (existing, `src/Synto.Generators/Templating/Builders/`, used by `FacadeCallFinder`).
- Produces: `internal sealed class QualificationFinder : TemplateScopedWalker { public static IReadOnlyDictionary<SyntaxNode, NameSyntax> FindQualifications(SemanticModel semanticModel, SyntaxNode body, TemplateScope scope); }`

- [ ] **Step 1: Write the failing test**

```csharp
// test/Synto.Test/Templating/QualificationFinderTest.cs
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Templating.Builders;
using Synto.Generators.Templating.Quoting;

namespace Synto.Test.Templating;

public class QualificationFinderTest
{
    private static (SemanticModel Model, MethodDeclarationSyntax Method) Compile(string body)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            class C<T>
            {
                void M()
                {
                    {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Q",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return (model, method);
    }

    private static IReadOnlyDictionary<SyntaxNode, NameSyntax> Find(SemanticModel model, MethodDeclarationSyntax method)
        => QualificationFinder.FindQualifications(model, method, TemplateScope.Root(method));

    [Fact]
    public void QualifiesFrameworkType_TypePosition()
    {
        var (model, method) = Compile("Type t = null; GC.KeepAlive(t);");
        var map = Find(model, method);
        // 'Type' -> global::System.Type
        Assert.Contains(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "Type" }
                                   && kv.Value.ToString() == "global::System.Type");
    }

    [Fact]
    public void QualifiesFrameworkType_StaticMemberAccessRoot()
    {
        var (model, method) = Compile("object o = DBNull.Value;");
        var map = Find(model, method);
        Assert.Contains(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "DBNull" }
                                   && kv.Value.ToString() == "global::System.DBNull");
    }

    [Fact]
    public void SkipsTypeParameter()
    {
        var (model, method) = Compile("T x = default;");
        var map = Find(model, method);
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "T" });
    }

    [Fact]
    public void SkipsVar()
    {
        var (model, method) = Compile("var y = 1;");
        var map = Find(model, method);
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "var" });
    }

    [Fact]
    public void SkipsLocalAndMemberNames()
    {
        var (model, method) = Compile("int z = 0; z.ToString();");
        var map = Find(model, method);
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "z" });
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "ToString" });
    }
}
```

Note: use the existing `TemplateScope.Root(...)` factory if its signature differs — check `TemplateScope` in `src/Synto.Generators/Templating/Builders/`; the finder just needs the root scope for a body with no nested `[Template]`. If no `Root` factory exists, mirror how `FacadeCallFinderTest` / `TemplateFactoryBuilder.ResolveBuilderCalls` obtains `ctx.Scope` and construct the equivalent root scope in the test helper.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class "*QualificationFinderTest*"`
Expected: FAIL — `QualificationFinder` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Synto.Generators/Templating/Quoting/QualificationFinder.cs
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Templating.Builders;

namespace Synto.Generators.Templating.Quoting;

/// <summary>
/// Discovers type/namespace-bound simple names in a <c>[Template]</c> body and records, per name node, its
/// <c>global::</c>-rooted fully-qualified substitute — so the quoter emits self-contained output (spec
/// 2026-07-10-quote-time-type-qualification). Only names that BIND to an <see cref="INamedTypeSymbol"/> as a
/// type/namespace reference are recorded; type parameters, <c>var</c>, non-type symbols (locals, members, builder
/// facades), already-<c>global::</c>-rooted names, and the Synto surface are skipped. A qualified name is keyed on
/// the OUTERMOST name node (its constituent segments are not descended into), so nested/generic names produce a
/// single substitute. Like <see cref="FacadeCallFinder"/> it is a <see cref="TemplateScopedWalker"/>: a nested
/// child <c>[Template]</c> subtree is skipped wholesale (the child qualifies itself). Runs inside the transform;
/// captures no symbol/model — only the substitute <see cref="NameSyntax"/> (a plain syntax value).
/// </summary>
internal sealed class QualificationFinder : TemplateScopedWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<SyntaxNode, NameSyntax> _qualifications = new();

    private QualificationFinder(SemanticModel semanticModel, TemplateScope scope)
        : base(scope) => _semanticModel = semanticModel;

    public static IReadOnlyDictionary<SyntaxNode, NameSyntax> FindQualifications(
        SemanticModel semanticModel, SyntaxNode body, TemplateScope scope)
    {
        var finder = new QualificationFinder(semanticModel, scope);
        finder.Visit(body);
        return finder._qualifications;
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.IsVar) return;             // never expand 'var' to its inferred type
        TryQualify(node);                   // identifier has no child names to descend
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        if (TryQualify(node)) return;       // whole generic name qualified (type args come from the symbol)
        base.VisitGenericName(node);        // else descend (e.g. type args that themselves need qualifying)
    }

    public override void VisitQualifiedName(QualifiedNameSyntax node)
    {
        if (TryQualify(node)) return;       // outermost qualified name wins; do not descend into segments
        base.VisitQualifiedName(node);
    }

    private bool TryQualify(SyntaxNode node)
    {
        // Already global::-rooted (parent alias-qualified) — leave as authored.
        if (node.Parent is AliasQualifiedNameSyntax)
            return false;

        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is not INamedTypeSymbol type)
            return false;                   // type parameters, methods, locals, members, unbound facades

        // Exclude the Synto surface (mirrors TransplantCarrierUsings' Synto/Synto.* exclusion).
        string ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns == "Synto" || ns.StartsWith("Synto.", StringComparison.Ordinal))
            return false;

        _qualifications[node] = QualifiedNameFactory.ForSymbol(type);
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class "*QualificationFinderTest*"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(templating): QualificationFinder records per-node global:: substitutes"
```

---

### Task 3: Wire qualification into the plan (`QualifyStep` + channel + fold + finder call)

**Files:**
- Create: `src/Synto.Generators/Templating/Quoting/Passes/QualifyStep.cs`
- Modify: `src/Synto.Generators/Templating/Emit/QuotePlan.cs` (add `Qualifications` channel)
- Modify: `src/Synto.Generators/Templating/Emit/PlanAssembler.cs:24-70` (add `qualifications` param + fold)
- Modify: `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs` (add `ResolveQualifications(ctx)` step; pass `ctx.Plan.Qualifications` to `BuildNodeSteps` at `QuoteAndAssemble` :949)
- Test: `test/Synto.Test/Templating/QualificationTemplateTest.cs`

**Interfaces:**
- Consumes: `QualificationFinder.FindQualifications` (Task 2); `INodeQuoteMiddleware`/`NodeQuoteNext`/`QuoteContext` (`Quoting/Passes/QuoteStep.cs`); `PlanAssembler.BuildNodeSteps`; `TemplateBuildContext` (`ctx.SemanticModel`, `ctx.Scope`, `ctx.Plan`, `ctx.TemplateInfo.Source!.Syntax`).
- Produces: `internal sealed class QualifyStep : INodeQuoteMiddleware`; new `QuotePlan.Qualifications` channel of type `Dictionary<SyntaxNode, NameSyntax>`; a new `BuildNodeSteps` parameter `IReadOnlyDictionary<SyntaxNode, NameSyntax>? qualifications`.

- [ ] **Step 1: Write the failing test**

```csharp
// test/Synto.Test/Templating/QualificationTemplateTest.cs
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

// Mirrors SwitchBuilderTest's harness: SurfaceInjection + TemplateFactory generators over a [Template] carrier.
public class QualificationTemplateTest
{
    private static readonly MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard, Version=2.0.0.0").Location),
        MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;

        partial class Factory {}

        [Template<Factory>]
        public class Carrier
        {
            public Type Describe()
            {
                Type t = typeof(int);
                object boxed = DBNull.Value;
                List<int> nums = new List<int>();
                GC.KeepAlive(nums);
                return t;
            }
        }
        """;

    [Fact]
    public void QualifiesFrameworkNamesInGeneratedFactory()
    {
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(Source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());
        var runResult = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        var factory = runResult.Results.Single(r => r.Generator.GetGeneratorType() == typeof(TemplateFactorySourceGenerator));
        var factorySource = factory.GeneratedSources
            .Single(g => g.HintName.StartsWith("Factory.", StringComparison.Ordinal))
            .SourceText.ToString();

        // The quoted names are emitted as string-literal identifiers inside SyntaxFactory calls; qualification
        // makes them the global:: form. Assert the factory quotes the fully-qualified names, not bare ones.
        Assert.Contains("global::System.Type", factorySource, StringComparison.Ordinal);
        Assert.Contains("global::System.DBNull", factorySource, StringComparison.Ordinal);
        // Generic name: the qualifier prefix is composed in front of the (base-quoted) simple name, exercising
        // the QualifyStep compose path — a QualifiedName wrapping the authored `List<...>`.
        Assert.Contains("global::System.Collections.Generic.List", factorySource, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class "*QualificationTemplateTest*"`
Expected: FAIL — factory emits bare `System.Type`/`DBNull` (no `global::`), assertion fails.

- [ ] **Step 3a: Add the `QualifyStep` middleware**

```csharp
// src/Synto.Generators/Templating/Quoting/Passes/QualifyStep.cs
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generators.Templating.Quoting.Passes;

/// <summary>
/// Node-pipeline step that emits the authored type name in its <c>global::</c>-rooted fully-qualified form while
/// PRESERVING any nested per-node steps (e.g. a <c>[Splice]</c> type argument's <see cref="ReplaceStep"/>). Rather
/// than re-quoting the baked substitute wholesale — which would discard the authored subtree and its splices — it
/// re-quotes only the authored FINAL SIMPLE NAME through the pipeline continuation <paramref name="next"/> (so any
/// nested holes still fire), then re-parents that quote under the substitute's holes-free qualifier prefix. Sound
/// because <paramref name="next"/> is the continuation PAST this step (no self-recursion) and the base quoter
/// descends into children via the overridden Visit, re-dispatching their steps. Holds only a
/// <see cref="NameSyntax"/> value, so it is cacheability-safe.
/// </summary>
internal sealed class QualifyStep : INodeQuoteMiddleware
{
    private readonly NameSyntax _qualified;   // global::-rooted fully-qualified substitute (QualifiedNameFactory.ForSymbol)

    public QualifyStep(NameSyntax qualified) => _qualified = qualified;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        // The authored node's final simple name carries the type arguments (and thus any [Splice] holes):
        // the node itself when it is a simple name, else the right-hand simple name of a written-out qualified name.
        SimpleNameSyntax authoredSimple = node as SimpleNameSyntax ?? ((QualifiedNameSyntax)node!).Right;

        // Quote it through the continuation so nested per-node steps still fire. Do NOT re-enter via ctx.Quote on
        // `node` — that re-dispatches this very step (recursion). `next` runs the chain PAST QualifyStep.
        ExpressionSyntax inner = next(authoredSimple)!;

        // Re-parent `inner` under the substitute's holes-free qualifier so the namespace / global:: prefix is
        // injected without baking over the authored (spliceable) simple name.
        return _qualified switch
        {
            QualifiedNameSyntax qn => Invoke(nameof(QualifiedName), ctx.Quote(qn.Left)!, inner),
            AliasQualifiedNameSyntax aqn => Invoke(nameof(AliasQualifiedName), ctx.Quote(aqn.Alias)!, inner),
            _ => ctx.Quote(_qualified),   // defensive: no qualifier to inject (bare/global-namespace simple name)
        };
    }

    private static ExpressionSyntax Invoke(string factoryMethod, params ExpressionSyntax[] args)
        => InvocationExpression(IdentifierName(factoryMethod))
            .WithArgumentList(ArgumentList(SeparatedList(args.Select(Argument))));
}
```

**Why compose (not `ctx.Quote(_qualified)`):** the substitute `_qualified` is fully baked from the symbol, so its
type-argument subtree is fresh nodes with no map entries — re-quoting it wholesale drops the authored inner `T`'s
`ReplaceStep` (the gen-1 defect; see the header deviation note). Decomposing `_qualified` into its qualifier
(`qn.Left` / `aqn.Alias`, both holes-free) and re-parenting `next(authoredSimple)` keeps the inner splice live.

- [ ] **Step 3b: Add the `Qualifications` channel to `QuotePlan`**

In `src/Synto.Generators/Templating/Emit/QuotePlan.cs`, alongside the other node-keyed channels (after `Replacements` at :21), add:

```csharp
    /// <summary>
    /// Per-node type-qualification channel: an authored bare type/namespace name node -> its global::-rooted
    /// substitute NameSyntax. Folded by PlanAssembler into a terminal QualifyStep. Populated by QualificationFinder.
    /// </summary>
    public Dictionary<SyntaxNode, NameSyntax> Qualifications { get; } = new();
```

Ensure `using Microsoft.CodeAnalysis.CSharp.Syntax;` is present in `QuotePlan.cs` (add if missing).

- [ ] **Step 3c: Fold the channel in `PlanAssembler.BuildNodeSteps`**

In `src/Synto.Generators/Templating/Emit/PlanAssembler.cs`, extend `BuildNodeSteps` (:24). Add the parameter and the fold. Place the `QualifyStep` append **after `replacements`** and before `builderCalls`. A `ReplaceStep` and a `QualifyStep` never land on the SAME node: `ReplaceStep` (a wholesale replacement) short-circuits without calling `next`, so if both were present Qualify would never run — but the finder only qualifies type-NAME nodes whereas replacements target the spliced inner nodes (e.g. the `T` INSIDE `IEnumerator<T>`), so they key on different nodes. That inner splice is exactly what `QualifyStep` preserves by composing through `next` (see Step 3a) — the outer qualified name and the inner replacement cooperate across parent/child, they do not collide on one node. Builder/trim/decorate likewise target invocations / statements / type decls, not type-name nodes.

Signature becomes:

```csharp
    public static Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware>> BuildNodeSteps(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements,
        IReadOnlyDictionary<SyntaxNode, NameSyntax>? qualifications,
        IReadOnlyDictionary<SyntaxNode, BuilderCall>? builderCalls,
        HashSet<SyntaxNode> trimNodes,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? decorationHooks)
```

Fold body — immediately after the `replacements` loop that appends `ReplaceStep` (:42-43), add:

```csharp
        if (qualifications is not null)
            foreach (var q in qualifications)
                StepsFor(q.Key).Add(new QualifyStep(q.Value));
```

Update the class precedence doc comment (:19-23) to `Replace → Qualify → Builder → Trim → Decorate`. Add `using Synto.Generators.Templating.Quoting.Passes;` if not already imported (it is — `ReplaceStep` lives there).

- [ ] **Step 3d: Run the `QualificationFinder` and pass the channel through in `TemplateFactoryBuilder`**

In `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs`:

1. Add a resolve step near `ResolveBuilderCalls` (defined :791-812). Add this method:

```csharp
    private static void ResolveQualifications(TemplateBuildContext ctx)
    {
        var map = QualificationFinder.FindQualifications(
            ctx.SemanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Scope);
        foreach (var q in map)
            ctx.Plan.Qualifications[q.Key] = q.Value;
    }
```

2. Call it in `Build` in the same phase as `ResolveBuilderCalls` (right after it — around :73). Add:

```csharp
        ResolveQualifications(ctx);
```

3. In `QuoteAndAssemble` (:942-964), pass the new channel into `BuildNodeSteps` (:949):

```csharp
        var nodeSteps = PlanAssembler.BuildNodeSteps(
            ctx.Plan.Replacements, ctx.Plan.Qualifications, ctx.Plan.BuilderCalls, prunableNodes, ctx.Plan.DecorationHooks);
```

4. Add `using Synto.Generators.Templating.Quoting;` to `TemplateFactoryBuilder.cs` if not present.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class "*QualificationTemplateTest*"`
Expected: PASS. If `BuildNodeSteps` has other call sites (e.g. `highlightsOnly` path), update them to pass the new argument (pass `null` for the highlights path if it does not resolve qualifications) — the compiler error will name them.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(templating): qualify type names at quote time via QualifyStep"
```

---

### Task 4: ObjectReader oracle — retire the interim hardcode, output self-contained by construction

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ObjectReaderGenerator.cs` (remove the two hardcoded `using`s added in `Emit()`)
- Modify (re-accept): `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs`

**Interfaces:**
- Consumes: the whole qualification feature (Tasks 1–3). The bare-authored `ReaderTemplate.cs` body (already de-qualified) now produces `global::`-qualified output; the interim hardcoded `using System;` / `using System.Collections.Generic;` in `Emit()` become redundant.

- [ ] **Step 1: Confirm the guard test currently passes on the hardcode (baseline)**

Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj -c Debug --filter-method "*Generated_Reader_Compiles_Without_ImplicitUsings*"`
Expected: PASS (the interim hardcode is still in place).

- [ ] **Step 2: Remove the interim hardcode**

In `ObjectReaderGenerator.cs` `Emit()`, delete the `.AddUsings(...)` call and its explanatory comment (the block added for C1), reverting to:

```csharp
        var unit = SyntaxFactory.CompilationUnit()
            .AddMembers(SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(GeneratedNamespace))
                .AddMembers(members.ToArray()))
            .NormalizeWhitespace();
```

- [ ] **Step 3: Run the compile gate — it must STILL pass (now via qualification, not the hardcode)**

Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj -c Debug --filter-method "*Generated_Reader_Compiles_Without_ImplicitUsings*"`
Expected: PASS — the generated reader is self-contained because the template body's `IEnumerator<T>`/`Type`/`DBNull`/exception names are now emitted `global::`-qualified. If it FAILS with CS0246, qualification is not covering some name — return to Task 2/3 and widen coverage before proceeding.

- [ ] **Step 4: Re-accept the ObjectReader snapshot**

Run the snapshot test, then accept:

```bash
dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj -c Debug --filter-method "*Generates_Specialized_Reader_For_Person*"
mv -f "examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.received.cs" \
      "examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs"
```

Inspect the new verified file: the body names must be `global::System.Collections.Generic.IEnumerator<...>`, `global::System.Type`, `global::System.DBNull`, etc., and the two top-of-file `using` directives from the interim hardcode must be GONE (qualification makes them unnecessary). This reproduces the pre-de-qualification `global::` shape (the spec's oracle).

- [ ] **Step 5: Full ObjectReader project green**

Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj -c Debug`
Expected: PASS (all tests, including the compile gate and the re-accepted snapshot).

- [ ] **Step 6: Commit**

```bash
jj commit -m "feat(objectreader): drop interim hardcoded usings; output self-contained via qualification"
```

---

### Task 5: Suite-wide snapshot re-accept + full green

**Files:**
- Modify (re-accept): every `*.verified.cs` under `test/Synto.Test/` (and `test/Synto.Diagnostics.Test/`, `test/Synto.Bootstrap.Test/` if affected) whose generated output names framework types — these churn bare→`global::`.

**Interfaces:**
- Consumes: the qualification feature. No new production code — this is regeneration + verification that every re-accept still compiles.

- [ ] **Step 1: Build the solution**

Run: `dotnet build Synto.slnx --no-restore -c Debug`
Expected: 0 errors.

- [ ] **Step 2: Run the full test suite; expect snapshot diffs only**

Run: `dotnet test Synto.slnx -c Debug --no-build`
Expected: failures are ONLY Verify snapshot mismatches (bare→`global::`). If any test fails for a non-snapshot reason (a post-gen compile assertion, an execution test, a cacheability assertion), STOP — that is a real regression; fix it before re-accepting anything.

- [ ] **Step 3: Review a representative diff before mass-accepting**

For one changed snapshot, diff `received` vs `verified` and confirm the only change is bare→`global::` qualification of type names (no structural change, no dropped members). This is the correctness gate on the mass accept.

- [ ] **Step 4: Accept all snapshot diffs**

For each `*.received.cs` produced, move it over its `*.verified.cs`. From the repo root:

```bash
find test examples -name "*.received.cs" -print0 | while IFS= read -r -d '' r; do
  mv -f "$r" "${r%.received.cs}.verified.cs"
done
```

- [ ] **Step 5: Re-run the full suite — all green**

Run: `dotnet test Synto.slnx -c Debug --no-build`
Expected: PASS (0 failed). The post-gen compile / execution / cacheability assertions embedded in the suite are the backstop confirming every re-accept is a valid, compiling qualification.

- [ ] **Step 6: Commit**

```bash
jj commit -m "test(templating): re-accept snapshots for global:: qualification (bare->global::)"
```

---

### Task 6: Cacheability verification

**Files:**
- Test: `test/Synto.Test/Templating/QualificationTemplateTest.cs` (extend from Task 3)

**Interfaces:**
- Consumes: `CacheabilityAssert.AllStepsCachedOrUnchanged` (`test/Synto.Test/CacheabilityAssert.cs:38`); the existing "unrelated edit" re-run pattern (`SimpleTemplateTest.cs:491-499`).

- [ ] **Step 1: Write the failing/guard test**

Add to `QualificationTemplateTest`:

```csharp
    [Fact]
    public void QualificationIsCacheable()
    {
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(Source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        var result = driver.RunGenerators(modified, TestContext.Current.CancellationToken)
            .GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.Transform });  // use the actual tracking name(s) the template pipeline exposes
    }
```

Note: use whatever tracking-name constant(s) the non-generic `[Template]` pipeline registers (see `TemplateTrackingNames` and how `SimpleTemplateTest` selects them for the bare-template path). The point is that adding `ResolveQualifications` inside the transform introduced NO new pipeline step and captured no model — the existing tracked steps must remain `Cached`/`Unchanged`.

- [ ] **Step 2: Run it**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method "*QualificationIsCacheable*"`
Expected: PASS. If it FAILS, the finder leaked a symbol/model into pipeline state or added an uncached step — fix the finder to emit only syntax values (it should already).

- [ ] **Step 3: Commit**

```bash
jj commit -m "test(templating): assert quote-time qualification preserves cacheability"
```

---

### Task 7 (optional cleanup): retire the dead M12 helper

**Files:**
- Modify/Delete: `src/Synto.Core/Symbols/SymbolExtensions.cs` (`GetQualifiedNameSyntax`)

**Interfaces:**
- None. `GetQualifiedNameSyntax` in Synto.Core has no callers (confirmed by the architecture map). This feature deliberately uses `FullyQualifiedFormat` instead, so the buggy helper is now unambiguously dead.

- [ ] **Step 1: Confirm no callers**

Run: `grep -rn "GetQualifiedNameSyntax" src/Synto.Core src/Synto.Generators test`
Expected: only the definition (and self-recursion) in `src/Synto.Core/Symbols/SymbolExtensions.cs`. (The `Synto.Bootstrap` copy is separate and DOES have callers — leave it.)

- [ ] **Step 2: Delete the dead method**

Remove `GetQualifiedNameSyntax` (and `GetNamespaceNameSyntax` if it too is unused after removal) from `src/Synto.Core/Symbols/SymbolExtensions.cs`. If the file becomes empty, delete it and drop any `<Compile>` reference.

- [ ] **Step 3: Build**

Run: `dotnet build Synto.slnx --no-restore -c Debug`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
jj commit -m "chore(core): delete dead GetQualifiedNameSyntax (M12); qualification uses FullyQualifiedFormat"
```

---

## Self-Review

**Spec coverage:**
- Spec §2/§3 (approach + finder→per-name-middleware, SemanticModel-free quoter) → Tasks 2, 3. ✓
- Spec §3.1/§3.2/§3.3 (finder, QualifyStep closing over NameSyntax, PlanAssembler fold) → Tasks 2, 3. ✓
- Spec §4 (scope predicate: skip type params/var/facades/predefined/already-global/Synto) → Task 2 tests + Global Constraints predicate. ✓
- Spec §5 (precedence owned by PlanAssembler; verbatim regions unaffected) → Task 3 Step 3c (placement + short-circuit note). ✓
- Spec §6 (one helper, generics/nested correct) → Task 1 via `FullyQualifiedFormat`; deviation documented (M12 not fixed, dead helper retired in Task 7). ✓
- Spec §7 (error handling: unbound names left as authored) → Task 2 `TryQualify` returns false when `GetSymbolInfo(...).Symbol` is null/non-type. ✓
- Spec §9 (oracle = pre-de-qual ObjectReader global:: snapshot; suite churn re-accept; focused tests; cacheability) → Tasks 4, 5, 6, and Task 2 focused tests. ✓
- Spec §10 (remove interim hardcode; re-accept; experimental branch) → Task 4 + Global Constraints (workspace). ✓
- Spec §8 non-goal (post-gen tidy pass) → not planned. ✓

**Placeholder scan:** no TBD/TODO/"handle edge cases" — each code step has full code. Two explicit "use the actual tracking-name / adapt the scope factory" notes (Task 2 Step 1, Task 6 Step 1) point at named existing constants the implementer must read rather than invent; acceptable, not a placeholder.

**Type consistency:** `QualifiedNameFactory.ForSymbol(ISymbol) : NameSyntax` used consistently (Tasks 1→2). `QualificationFinder.FindQualifications(SemanticModel, SyntaxNode, TemplateScope) : IReadOnlyDictionary<SyntaxNode, NameSyntax>` used in Tasks 2→3. `QualifyStep(NameSyntax)` matches the channel value type. `BuildNodeSteps` new parameter `IReadOnlyDictionary<SyntaxNode, NameSyntax>? qualifications` matches `QuotePlan.Qualifications` (`Dictionary<SyntaxNode, NameSyntax>`). ✓
