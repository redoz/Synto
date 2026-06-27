# Design: `[Splice]` on a method — the member-declaration generator

**Date:** 2026-06-28
**Status:** APPROVED (brainstormed, owner-approved 2026-06-28)
**Builds on:** the quote/unquote/splice vocabulary (`2026-06-27-quote-unquote-splice-naming.md`,
landed on `experimental`) and the `[Quote]` value-marker (`2026-06-28-quote-value-marker.md`,
landed on `experimental`). This adds the **member axis** to `[Splice]`.

## 1. Problem

Templating today produces **one member's worth** of syntax at a time. A `[Template]` method's body
is quoted; a `[Template]` class is quoted whole (or as a `SyntaxList<MemberDeclarationSyntax>` under
`Bare`). The staging engine can unroll control flow **inside a single member body**
(`ForeachOverStagedParameter_UnrollsToIfChain`, `SharedParameter_AcrossMembers_BothUnroll`), but there
is no way to say **"loop over factory-time data and emit *N members*"** — one `GetXxx`/`ReadXxx` method
per column, a nested type per row-shape, an enum case table, etc. C# has no loop construct at
class-body level, so the member count cannot vary from a quoted template alone.

The ObjectReader dog-food hits this wall directly: it can unroll a `foreach` into an if-chain *inside*
`GetName`, but it cannot generate the per-column accessor *members* themselves.

## 2. The conceptual model (why this is `Splice`, not `Unquote`)

By the established vocabulary:

- **Quote** — lift a factory-time *value* into its syntax (a literal / converter call).
- **Unquote** — escape to factory time; the inside is **ordinary C#** whose runtime *value* is lifted
  back into the tree. You never touch a `SyntaxNode`.
- **Splice** — insert a **pre-built syntax node verbatim**; no value-lift. The thing already *is*
  syntax.

A method that **returns a `MemberDeclarationSyntax`** is handing over a node that is already syntax, to
be dropped into the enclosing type verbatim. That is **splice**, not unquote — the body doing
factory-time work to *build* the node does not change it, exactly as `Factory.Inner().Expression` is
computed and then spliced. An `[Unquote]` that returned a node would be a contradiction in terms
(unquote's inside is normal C#, not syntax). So this feature is the **member-axis form of `[Splice]`**:
`[Splice]` already means "a node provided to be inserted"; today that node arrives as a *parameter*
(`[Splice] ExpressionSyntax`) or an inline call (`Splice(node)`); this adds the node arriving as the
*return of a factory-time generator method*, spliced at **member** position.

**Distinct from `[SyntaxBuilder]`.** `[SyntaxBuilder]` is *consumer-driven*: you call it **by name**
from a template body, Synto synthesizes an inert facade and rewrites the call site, and it returns an
`ExpressionSyntax`/`TypeSyntax` (SY1017). The member generator is *generator-driven*: it is
**auto-invoked** to populate the enclosing type, has no call site and no facade, and returns
*members*. They are different invocation models; the member generator is not routed through the builder
pipeline.

## 3. Contract for `[Splice]` on a method

`[Splice]` is widened from `AttributeTargets.Parameter` to also target `Method`. A `[Splice]`-marked
**static** method declared inside a `[Template]` type is an **auto-invoked, factory-time member
generator**:

- **Return type (the dispatch).** It must return `MemberDeclarationSyntax` **or**
  `IEnumerable<MemberDeclarationSyntax>`. The return type is what selects the **member** splice site
  (contrast the expression/statement splices already keyed by type). The contract is keyed on the
  **base `MemberDeclarationSyntax`** deliberately (§6.4) — that is what lets a generator emit nested
  types, fields, properties, delegates, etc., not just methods.
- **Body is ordinary factory-time C#.** It obtains staged inputs via `Parameter<T>()`
  (`var columns = Parameter<IReadOnlyList<Col>>();`), loops, and produces members — the on-paradigm way
  being to call a per-member `[Template]` (a `[Template(Options = Bare | Single)]` method already
  returns a `MethodDeclarationSyntax`) and `yield return` / collect the results. Hand-built
  `SyntaxFactory` stays available as the raw escape hatch but is not the headline path.
- **Auto-invoked, spliced in place.** Synto invokes the generator at factory-build time and splices its
  returned member(s) into the produced type's member list **at the generator method's position among
  its siblings** (so order is the author's declaration order). Quoted fixed members on either side are
  preserved.
- **The generator method is trimmed from the output.** It is factory-time scaffold; it does not appear
  as a member of the generated type.

**Behavioral shape (the point):**

```csharp
[Template(typeof(Factory))]
partial class Reader {
    [Splice]                                              // auto-invoked member generator
    static IEnumerable<MemberDeclarationSyntax> Accessors() {
        var columns = Parameter<IReadOnlyList<Col>>();    // folds into the Factory signature
        foreach (var c in columns)
            yield return Factory.GetColumn(c);            // per-member [Template], returns a member
    }
    // ...fixed quoted members may sit alongside; order preserved...
}
// Factory.Reader(columns) → a Reader type with one accessor member per column spliced in.
```

## 4. Integration (reuses proven machinery)

Pin the contracts; the implementer chooses the exact emission.

- **`Parameter<>()` folds in for free.** The generator's `Parameter<T>()` calls are discovered by the
  existing `StagedParameterFinder` and deduped by `(name, type)` across all members, so they collapse
  into the **same** `Factory(...)` parameter the rest of the template uses (the
  `SharedParameter_AcrossMembers_BothUnroll` mechanism).
- **Member splice = the existing `BuildList`, lifted to the member axis.** Class templates with live
  regions already replace a container with
  `CollectionSyntaxExtensions.BuildList<StatementSyntax>(Run(__run), …fixed…)`. The same pattern at
  member scope — `BuildList<MemberDeclarationSyntax>(Run(__generated), …quoted fixed members…)` —
  assembles the type's member list. Proven pattern, new element type.
- **Emit-and-call, not inline.** The generator is emitted as a real `static` method that the factory
  invokes (with its `Parameter<>()` references resolved to the factory parameters); the factory `Run`s
  the result into the member `BuildList`. Inlining the body is rejected: it breaks on `yield return` and
  arbitrary control flow.
- **Cacheability is sacred.** All analysis runs inside the `ForAttributeWithMetadataName` transform of
  `TemplateFactorySourceGenerator`; only the equatable `TemplateGenerationResult` (generated text +
  `EquatableArray<DiagnosticInfo>`) flows out. No `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode`
  in pipeline state.

## 5. Rules + diagnostics (new descriptors, SY1019+)

These are genuinely new error conditions (unlike `[Quote]`, which reused `SY1011`/`SY1012`), so they
warrant new `SY####` descriptors (next free is **SY1019**):

1. **Non-static.** `[Splice]` on an instance method → diagnostic. A non-static generator could reach the
   template type's *output-world* instance members from factory-time code — a stage-boundary violation
   that type-checks but is meaningless. Static is what enforces the boundary; output-world references
   are emitted *as syntax* (e.g. a `Member`/`TypeOf` builder or `Splice`), never touched as an instance.
2. **Invalid return type.** A `[Splice]` method whose return type is not `MemberDeclarationSyntax` /
   `IEnumerable<MemberDeclarationSyntax>` → diagnostic naming the allowed set.
3. **Has parameters.** A `[Splice]` generator method is auto-invoked — there is no caller to supply
   arguments — so it must be parameterless; inputs come via `Parameter<>()`. A parameter list →
   diagnostic.

**No placement policing.** If a generator returns a node that is technically a `MemberDeclarationSyntax`
but illegal in type-member position (a `GlobalStatementSyntax`, a `NamespaceDeclarationSyntax`), it
simply produces invalid C# the **post-generation compile catches** as an ordinary CS error. The Synto
contract is "it is a member declaration"; misplacement fails the compile gate naturally, with no extra
descriptor.

## 6. Resolved design decisions (owner, 2026-06-28)

1. **Marker is `[Splice]` widened to `Method`** — not `[Unquote]`-on-a-method and not `[SyntaxBuilder]`.
   Returning a pre-built node is splice semantics (§2); `[Unquote]`-on-a-method is the *other* feature
   (§8) and `[SyntaxBuilder]` is the wrong invocation model (§2).
2. **Auto-invoked, parameterless, static** — the generator populates its enclosing type; it is not
   called by name. Inputs via `Parameter<>()`; output spliced at the declaration position.
3. **Per-member shape via composition.** The headline authoring path is a per-member `[Template]`
   (`Bare | Single` → a member node) stitched by the generator loop; raw `SyntaxFactory` remains an
   escape hatch, not the recommended path.
4. **Return contract keyed on the `MemberDeclarationSyntax` base**, deliberately — this is what unlocks
   **nested types** (`class`/`struct`/`record`/`interface`/`enum` all derive from it), delegates,
   fields, and properties from the same feature with no extra work. Narrowing to
   `MethodDeclarationSyntax` would foreclose that.

## 7. Testing (contracts to pin)

- **Headline compile-assert.** A `[Splice]` generator over `Parameter<IReadOnlyList<Col>>()` produces a
  type with **one member per column**; generator diagnostics empty, factory exists, and the
  **post-generation compile has no errors**. Assert the factory takes the folded `columns` parameter and
  the emitted type has N members (not a single quoted method).
- **Nested-type case.** A generator that `yield return`s a **nested type** (e.g. a `record` per element)
  splices nested types alongside methods — locks the `MemberDeclarationSyntax`-base contract (§6.4).
- **Order preservation.** Generated members land at the generator's declaration position among fixed
  quoted siblings.
- **Diagnostics.** Each of the three rules (§5) fires on its violation and does not fire on the valid
  shape.
- **Cacheability.** A `[Splice]`-member template is incremental on an unrelated edit — **every** tracked
  step `Cached`/`Unchanged` (not terminal-only), per `CacheabilityAssert`.
- **Dog-food.** An ObjectReader-shaped example generating the per-column accessor members end to end.

## 8. Non-goals / explicit successor

- **`[Unquote]`-on-a-method** — the *normal-C#* member that unrolls into many members via
  **declaration-position staging** (varying member *names*/return types, `Get{c.Name}`). That is the
  genuinely-Unquote dual of this feature and a materially larger piece (declaration-position staging is
  new capability). It is the explicit next spec; noted here so it is not lost.
- **Async is out of scope here.** The generator is a synchronous transformation; async
  input-gathering happens at the caller and arrives via `Parameter<>()`. If async proves worthwhile it
  will be added as a **cross-cutting later plan** across all surfaces where it makes sense (not bolted
  onto this feature). The generator body is emitted into the consumer's factory and runs when *they*
  invoke it — if that is inside their own source generator, async would be sync-over-async, so the
  default stays sync.
- No change to `[Unquote]` / `[Quote]` / parameter-`[Splice]` / `[SyntaxBuilder]` semantics or names.
- No re-routing of member generation through the `[SyntaxBuilder]` facade pipeline.
