# Ambient sub-template scope — design

**Status: DRAFT (2026-07-09).** Pitched at contracts and behaviours, not exact code. Builds directly on
the shipped *Sub-templates + generic `Template<T>`* model
(`docs/superpowers/specs/2026-07-04-sub-templates-and-generic-template-attribute-design.md`). Changes the
**default lowering** of a nested `[Template]` so it shares its owner's scope; the current shipped lowering
becomes an explicit opt-out.

## 0. Origin

Dog-food friction (`objectreader-dogfood-drives-features`). The ObjectReader child `TypedGetter`
(`examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs:116-123`) is
invoked once per CLR type by the `[Splice] TypedGetters()` generator
(`ReaderTemplate.cs:125-140`). Every call re-threads the owner's `columns` live root
(`ReaderTemplate.cs:50`) as an explicit argument — `Factory.TypedGetter<bool>(columns, "…", "…")` — even
though `columns` is invariant across all 12 getters and already lifted once onto the owner factory. The
child is a genuinely owner-private helper, yet it is emitted as an isolated, independently-addressable
factory method with a hand-threaded input. This spec removes that threading for the common case.

The exploration that produced this design also *rejected* a larger surface: an explicit
`Bind<T>(Scope.Parent)` capture call plus a signed-depth `Scope` enum (`Parent = -1`,
`Ancestry = int.MinValue`). Making sub-templates ambient **by default** supersedes it — a co-located child
just references the owner binding by name — and the "one owner, no grandparents" rule leaves the depth enum
nothing to range over. Net: this is a *smaller* feature than the capture surface it replaces — a lowering
change plus one attribute bit, no new call surface. **[NOTE — Task 3 spike (resolved): "no new call surface"
HOLDS. The author drops the owner argument from the existing `Factory.X<T>(…)` call; the generator appends
that binding as an OPTIONAL trailing factory param (World-A bind target) and injects the arg in output. A
closure/nested-local lowering would instead delete the top-level member and break self-host binding — rejected.
See §8.2 (resolved).]**

## 1. Context — what exists today

- **Non-generic `[Template]` is structurally a sub-template.** A top-level `[Template]` with no target is
  an error (**SY1033**); a nested `[Template]` inside a `[Template<T>]` carrier is a sub-template that
  inherits the parent's target (`src/Synto.Core/Templating/Definition/Surface/TemplateAttribute.cs:5-15`).
  The generic `TemplateAttribute<T>` (`:17-28`) pins the factory target and is the root/top-level form.
  Both forms today share `TemplateOption Options` and `TemplateExtent Extent` properties.
- **A nested `[Template]` is lowered as its OWN top-level `internal static` factory method.**
  `ChildTemplateFinder` discovers it; `TemplateFactoryBuilder.Build` is reused verbatim to emit a standalone
  `MethodDeclarationSyntax` onto the shared target class (`internal static Factory.TypedGetter(…)`; see
  `test/Synto.Test/Templating/snapshots/ChildTemplateTest.ChildTemplate_MemberGeneratorInvokesChildFactory#Factory.TypedGetter.g.verified.cs:14-118`).
  It is **not** nested inside the owner's factory method. `internal` (not `private`) is what lets the
  owner's `[Splice]` source keep naming `Factory.TypedGetter(…)` across the class boundary (sub-templates
  spec AMENDMENT).
- **The owner's inputs become params of the owner's factory method.** `Factory.Reader(IReadOnlyList<Col>
  columns)` — `columns` is a *method param* of the owner factory (`…#Factory.Reader.g.verified.cs:13`).
- **A `[Splice]` generator already lowers to a nested local function that closes over those params.**
  `__spliceGenerator_Getters_0()` is emitted inside `Factory.Reader(…)` and reads the enclosing
  `columns` for free (`…#Factory.Reader.g.verified.cs:15-27`). So closure over owner scope is a lowering
  shape Synto already ships and already caches.
- **The parent→child ownership boundary is enforced structurally.** `TemplateScope` marks a nested child's
  subtree "foreign" so the parent's staging walk skips it wholesale — the child's own staged roots /
  params / type-params never leak into the parent factory signature
  (`src/Synto.Generators/Templating/Emit/TemplateScope.cs:9-23`; `TemplateScopedWalker`).

**The mechanical constraint that forces this design:** a top-level `static` method cannot close over another
method's locals. So a child can see the owner's *invariant* bindings ambiently **only** if it is lowered as
a nested local function inside the owner's factory. "Addressable top-level method" and "true ambient owner
scope" are mutually exclusive; you pick one per sub-template.

## 2. Goals

1. **Default a nested `[Template]` to ambient owner scope.** Lower it as a nested local function inside the
   owner's factory method so the owner's *class-level* bindings are lexically visible with no
   re-declaration and no threading, and sibling sub-templates call each other by bare name.
2. **Preserve today's shipped lowering as an explicit opt-out** — `[Template(Static)]` — for a sub-template
   that must be an addressable, standalone, reusable factory method.
3. **Keep the opt-out off the root.** The bit lives only on the non-generic `TemplateAttribute`; a
   `[Template<T>]` root cannot express it (type-system-enforced, not a runtime check).

### Non-goals

- **No `Bind`/`Scope` capture surface.** Dropped in favour of ambient-by-default (see §0).
- **No multi-level ancestry.** One owner depth only: an owner and its sibling children. Grandparent
  capture / cross-family binding is out of scope; there is no ≥2-level nesting anywhere in the codebase to
  drive or test it.
- **No change to root (`[Template<T>]`) generation.**
- **No ambient capture of a sibling sub-template's *own* params.** Only the owner carrier's class-level
  bindings are ambient (§3). A sibling's method-scoped inputs are not visible and are not captured.
- **No cross-carrier reuse of an ambient sub-template.** Ambient = owner-private by construction. Reuse is
  what `[Template(Static)]` is for.

## 3. What is "ambient" — the capture set

Exactly the owner **carrier's class-level bindings**, because those are the ones a nested method can both
(a) name in carrier source and (b) find in the owner factory method's scope after lowering:

- the carrier's staged **type parameter(s)** — e.g. `[Splice] T` on `ObjectReaderTemplate<[Splice] T>`;
- **field-position staged roots** — a single-declarator `= Unquote<…>()` field, e.g.
  `static readonly EquatableArray<ColumnInfo> columns = Unquote<…>()` (`ReaderTemplate.cs:50`).

**Not** ambient: any input local to a *sibling* template method (it is neither visible in carrier source nor
in the owner factory method's scope). Those remain out of reach — matching the existing `TemplateScope`
foreign-subtree boundary.

Only **invariant** owner bindings benefit. A sub-template's **varying** inputs (per-invocation values such
as `TypedGetter`'s `TRet`, `clrTypeName`, `typeLabel`) stay explicit `[Unquote]`/`[Splice]` params/type-
params on the child and are supplied at each call site as they are today.

**Name collisions** resolve by ordinary C# lexical shadowing: a child local (including its own param) named
`columns` shadows the owner's `columns`. No new rule.

## 4. Surface

Non-generic `TemplateAttribute` (sub-template only) gains a single opt-out bit. Working name `Static`
(bikeshed: `Isolated` / `Standalone` describe the *semantics*; `Static` describes the emitted mechanism —
resolve in the plan):

```csharp
public sealed class TemplateAttribute : Attribute        // sub-template ONLY
{
    public TemplateOption Options { get; set; }
    public TemplateExtent Extent  { get; set; }
    public bool Static { get; set; }                     // NEW — opt out of ambient; emit today's isolated form
}

public sealed class TemplateAttribute<T> : Attribute     // root — UNCHANGED, no Static bit
{
    public TemplateOption Options { get; set; }
    public TemplateExtent Extent  { get; set; }
}
```

The bit is a **dedicated property on the non-generic attribute**, deliberately **not** a `TemplateOption`
flag: `TemplateOption` is shared by both attribute forms, so a flag there would re-leak the concept onto the
root. A property on `TemplateAttribute` alone makes "a root cannot be `Static`-marked" structural.

Authoring (default vs opt-out):

```csharp
// DEFAULT — ambient. Child names the owner's `columns` directly; no param, no Bind.
[Template]
public TRet TypedGetter<[Unquote] TRet>([Unquote] string clrTypeName, [Unquote] string typeLabel, int i)
{
    Switch(i, columns.Where(c => c.ColumnTypeName == clrTypeName).Select(...));  // `columns` is ambient
    throw new InvalidCastException($"Field {i} is not {typeLabel} column.");
}
// [Splice] caller drops ONLY the columns arg (keeps the `Factory.` qualifier + generic form):
//   Factory.TypedGetter<bool>("global::System.Boolean", "a Boolean")
// ^ RESOLVED (Task 3 spike): the earlier bare `TypedGetter(typeof(bool), …)` form does NOT self-host-bind.
//   Instead the generated factory carries `columns` as an OPTIONAL trailing param so the columns-dropped call
//   binds in World A; SpliceGeneratorBodyRewriter injects the `columns` arg in World B. See §8.2 (resolved).

// OPT-OUT — today's shipped isolated form: top-level internal static Factory.TypedGetter(columns, …).
[Template(Static = true)]
public TRet TypedGetter<[Unquote] TRet>([Unquote] EquatableArray<ColumnInfo> columns, /*…*/ int i) { … }
```

## 5. Lowering

Two lowerings, selected by the `Static` bit:

- **Ambient (default).** Emit the sub-template as a **nested local function inside the owner's factory
  method**, at/above the point of first use, so it closes over the owner factory's params (the lifted
  class-level bindings). Sibling ambient sub-templates are peer local functions in the same method and call
  each other by bare name. Call sites in the owner's body / `[Splice]` generators reference the child by
  bare name (~~the generic-arg → leading `TypeSyntax` rewrite that already fires is unchanged~~ — **CORRECTION,
  Task 3 spike: no such rewrite exists; see §8.2 Finding A**; only the ambient owner args are *not* passed,
  because they are in scope). This is the **same lowering shape as an existing `[Splice]` generator**
  (`…#Factory.Reader.g.verified.cs:15-27`). **But how a self-hosted `[Splice]` NAMES the child is the
  unresolved blocker — §8.2 Finding B (owner sign-off pending).**
- **`Static` (opt-out).** Exactly today's path: `TemplateFactoryBuilder.Build` reused verbatim to emit a
  standalone `internal static` method on the target class; owner inputs are explicit params supplied at each
  call site.

`TemplateScope`/`TemplateScopedWalker` still exclude the child subtree from the parent's *staging* walk in
both modes — the boundary that keeps the child's own roots from leaking up is orthogonal to where the child
is *emitted*.

**Ambient vs `Static` trade, for the record:** ambient buys zero-threading + sibling calls + owner-scope
visibility, at the cost of owner-privacy (no `Factory.X` address, no standalone reuse, inputs implied by the
body rather than the signature). `Static` is the inverse. Default is ambient because a nested `[Template]`
is, by SY1033, already owner-bound — privacy is the honest default and matches the dog-food.

## 6. Validation the plan must carry

1. **Cacheability parity.** Ambient lowering is the same nested-local-closing-over-owner-params shape a
   `[Splice]` generator already ships and caches. The plan must confirm the incremental-generator
   cacheability guard (`CacheabilityAssert.AllStepsCachedOrUnchanged`, `cacheability-guard-iterate-all-steps`)
   stays green — no `ISymbol`/`SemanticModel`/`SyntaxNode` captured into cached state by the new lowering.
2. **Semantic-equivalent output on the flip.** Flipping the default re-lowers existing nested `[Template]`s.
   The plan must inventory every nested `[Template]` in tree (ObjectReader `TypedGetter`, `ChildTemplateTest`)
   and show generated output stays *semantically* equivalent with green tests — not necessarily byte-
   identical (`generated-output-semantic-not-byte-identical`). Snapshot re-accepts for the ambient shape
   (bare calls, local-function placement) are expected and benign.
3. **Dog-food drives it.** Validate by **building the ObjectReader example**, not only `Synto.Test`
   (`dogfood-validate-by-building-example`): the ambient `TypedGetter` must drop its `columns` param, the
   `[Splice]` loop must drop the `columns` argument, and the example's 10 tests must stay green.
4. **`Static` round-trips to today.** A `[Template(Static = true)]` sub-template must emit byte-identical to
   the currently-shipped isolated output (it *is* the current path) — a regression pin.

## 7. Migration

Pre-GA, clean break (`project-phase`). The default changes; consumers who relied on the isolated,
addressable form add `[Template(Static = true)]`. In-tree that is exactly the two known nested templates if
either needs to stay addressable — but ObjectReader's `TypedGetter` is *meant* to go ambient (that is the
whole point), and `ChildTemplateTest` should gain coverage of **both** modes. No compat shim.

## 8. Open decisions (resolve in the plan)

1. **Opt-out name:** `Static` (mechanism) vs `Isolated`/`Standalone` (semantics). Leaning `Static` per the
   owner's phrasing; confirm.
2. **RESOLVED by the Task 3 spike (2026-07-09; owner-approved same day — mechanic = auto-threaded optional
   trailing param, see the end of this item).** The call-referencing mechanic the ambient lowering needs did
   NOT exist yet, and the
   spec assumed a rewrite that also does not exist. Grounded findings:

   **Finding A — there is no generic-arg → `TypeSyntax` call rewrite. Two separate, already-shipped
   mechanisms produce the two shapes in the snapshots; neither is a call-site rewrite:**
   - `[Splice] TRet` (the `ChildTemplateTest` `GetterTemplate`): the child factory is emitted **non-generic**
     — `public static MethodDeclarationSyntax TypedGetter(TypeSyntax TRet, …)`
     (`…#Factory.TypedGetter.g.verified.cs:16`). The `[Splice]` caller **authors the `TypeSyntax` positional
     argument by hand** in source — `Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, …)`
     (`ChildTemplateTest.cs:78,80`). The `PredefinedType(…)` seen in `…#Factory.Reader.g.verified.cs:17-27`
     is that source copied **verbatim**, not a generator rewrite.
   - `[Unquote] TRet` (ObjectReader): `StagedTypeParameterFinder` classifies `[Unquote]` type params as
     `IsSplice = false`, so `TemplateFactoryBuilder.LiftStagedTypeParameters` (`TemplateFactoryBuilder.cs:225-256`)
     emits a **generic** factory `Factory.TypedGetter<TRet>(EquatableArray<ColumnInfo> columns, string, string)`
     with a `typeof(TRet).ToTypeSyntax()` preamble. The caller writes `Factory.TypedGetter<bool>(columns, …)`
     (`ReaderTemplate.cs:128`); the generic arg stays a **verbatim** generic arg into output, and the
     `TypeSyntax` conversion happens at factory **runtime** inside the child. No call-site rewrite.
   - The only rewriter of a `[Splice]` body, `SpliceGeneratorBodyRewriter`
     (`SpliceMemberGeneratorEmitter.cs:85-129`), touches **only** `Unquote` (`IsParameterCall`) — decls
     dropped, `Unquote("name")` → `IdentifierName(name)`. It never visits child-factory invocations. So spec
     §5 ("the generic-arg → leading `TypeSyntax` rewrite that already fires") and this §8.2's old wording
     describe a seam that does not exist. (This is why an earlier investigator could not locate it.)

   **Finding B — the blocker: ObjectReader self-hosts, so ambient deletes the very symbol the carrier binds
   to.** `Synto.Example.ObjectReader.Generator.csproj` references `Synto.Generators` with
   `OutputItemType="Analyzer"` **on its own project** — the generator runs on its own source. So the carrier's
   real-C# `[Splice] TypedGetters()` body (`ReaderTemplate.cs:125-140`) must **bind at self-host compile time**
   against the generator-produced `Factory.TypedGetter` in the **same** compilation. Self-host means World A
   (carrier compile) and World B (generated output) are ONE generated artifact. Ambient lowering removes the
   top-level `Factory.TypedGetter` member (emitting a nested local function instead) — which deletes the only
   symbol the carrier's `[Splice]` body binds to, and the generator's own source stops compiling. **Route (a)
   ("keep `Factory.X` nameable AND rewrite its calls to the bare local-fn in output") is impossible under
   self-host:** one artifact cannot both be a top-level `Factory.TypedGetter` (World-A binding) and not be one
   (ambient nested local, World-B). This is exactly §1's own mechanical constraint ("addressable top-level
   method" ⊥ "true ambient owner scope"), applied to the same member. The spec §4 authoring line
   (`TypedGetter(typeof(bool), "…", "…")` — bare, no `Factory.`) is likewise **not World-A-compilable** as
   written: no such symbol exists in the `[Splice]` static method's scope.

   **Why the two intuitive lowerings both fail:**
   - **Closure (nested local function, the framing §1/§5 assumed).** Lowering the child as a nested local
     function inside `Factory.Reader(…)` to close over `columns` **deletes** the top-level `Factory.TypedGetter`
     member — so the self-hosted `[Splice]` call no longer binds in World A (Finding B). It is also strictly
     **more** machinery, not less: World-A binding still needs a stand-in symbol, so closure = a stand-in **plus**
     a local function. Rejected.
   - **A separate inert bind-stub (nested `Factory.Getters` class + `using static`, or an injected facade).**
     A local function shadows a `using static`-imported method of the same name with no ambiguity — *proven* by
     probe: the same bare `TypedGetter<int>(…)` binds to the local function where one is in scope and to the
     `using static` stub where none is. So it is mechanically viable. **But it introduces a bind-only member the
     author is expected to call *instead of* the real factory — an unusable phantom.** Rejected on surface
     cleanliness.

   **Resolved mechanic — auto-thread owner bindings as OPTIONAL trailing factory parameters; no phantom, no
   closure, no new marker.** The child template drops the owner binding from its own signature and reads the
   owner's class-level binding directly in its body (exactly as the parent's own members already read the
   `columns` field). The generator, seeing an owner class-level binding referenced in the child body, **appends
   it as an optional trailing parameter** on the generated child factory and **injects the argument at each
   self-hosted call site**:

   ```csharp
   // authored child — no `columns` parameter; body reads the owner field ambiently
   [Template]
   public TRet TypedGetter<[Unquote] TRet>([Unquote] string clrType, [Unquote] string typeLabel, int i) { … columns … }

   // generated factory — owner binding appended as an OPTIONAL trailing param (still generic per the [Unquote] TRet path):
   public static MethodDeclarationSyntax TypedGetter<TRet>(string clrType, string typeLabel,
                                                          EquatableArray<ColumnInfo> columns = default) { … }
   ```
   - **World A** (carrier `[Splice]` self-host compile): the author writes `Factory.TypedGetter<bool>("…","…")`
     (owner arg omitted) → binds to the **real, usable** factory with `columns` defaulted (inert at
     carrier-compile). No phantom, no new call surface — the same `Factory.X<T>(…)` shape, one fewer argument.
   - **World B** (generated output): `SpliceGeneratorBodyRewriter` (extended) recognizes a call to an ambient
     child factory and **appends the owner-binding argument** → `Factory.TypedGetter<bool>("…","…", columns)`,
     binding the real member and passing the owner factory's `columns`. Byte-for-byte today's output.

   The bind target **is** the real factory — fully callable from anywhere with `columns` passed explicitly; the
   optional default exists only so the terse authored form compiles. The `[Unquote] TRet` generic path is
   unchanged (`TypedGetter<bool>(…)` binds the generic member, `typeof(TRet).ToTypeSyntax()` inside), so **no
   positional-`TypeSyntax` lift is ever needed** — Finding A's nonexistent rewrite stays nonexistent.

   **Cost (the one wart):** the owner binding becomes an **optional trailing parameter** (`= default`) and moves
   to last. A *human* hand-calling the factory bare would get an empty `columns` → wrong output; the generator
   never does, since it always injects the argument. The footgun is at least visible in the signature.

   **Consequence for the plan:** §0's "no new call surface" **holds** (no marker, no facade, no stub) — the
   author simply drops the owner argument from the existing call. The ambient default needs (1) the generator to
   append owner class-level bindings as optional trailing params on the child factory, and (2) a
   `SpliceGeneratorBodyRewriter` extension to inject those args at ambient-child call sites. **No closure, no
   nested-local-fn lowering, no `TemplateScopedWalker` surgery.** Phase 1 (the `Static` bit, Tasks 1–2) is
   unaffected. Tasks 4–5 are re-scoped to this mechanic.
3. **Diagnostic for a dead-ambient reference:** if an ambient child names an identifier that is neither its
   own input nor an owner class-level binding, is that today's ordinary C# "does not exist in context" error
   (preferred — no new diagnostic) or a Synto-specific SY10xx? Default: rely on the C# compiler.
4. **`ChildTemplateTest` shape:** split into ambient-default + `Static` cases; decide which becomes the
   primary snapshot.
