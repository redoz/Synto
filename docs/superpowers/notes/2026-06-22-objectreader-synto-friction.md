# ObjectReader → Synto friction log

Living log of where building the ObjectReader dog-food example against Synto-as-is hurt.
Feeds a separate Synto-improvement spec (spec §11). One entry per finding: what hurt, why,
"Synto could make this easier by …".

## Summary — ranked top findings (seeds for the Synto-improvement spec)

Building a real, type-specialized `IDataReader` generator with Synto worked: the **invariant** skeleton
templated cleanly and `Synto.Diagnostics` carried the SOR* descriptors. The friction clusters in two places —
expressing **data-driven** (list-shaped) output, and the **cacheability boilerplate** a generator author must
hand-roll. Ranked by expected payoff:

1. **A list/repeater hole for data-driven members.** The single biggest wall: a `[Template]` is a fixed shape and
   a Synto hole splices ONE node, so `FieldCount` + the four `switch` methods (variable arm count, one per column)
   fell back to raw `SyntaxFactory`. *Synto could* offer a hole that maps a collection to a `SeparatedList`
   (a data-driven cousin of the `Syntax statement; statement();` repeater), or a `Pattern.Replace`-style splice on
   a placeholder arm. (Task 4)
2. **A generator-author "cacheability toolkit."** Every cacheable incremental generator re-authors the same value
   types. Here that was `EquatableArray<T>`, `DiagnosticInfo`, and `LocationInfo` (and, pre-Synto, an
   `IsExternalInit` polyfill). Synto owns all of these internally but injects none to a generator author.
   *Synto could* inject a tiny cacheability surface the way it already injects the templating markers. (Tasks 2–4)
3. **Template parameters for the emitted shell (name / modifiers / base-list).** A `[Template]` can't carry a
   `file` modifier or a per-call-site type name, so every quote is followed by `.WithIdentifier` (class AND ctor),
   `.WithModifiers(file sealed)`, `.WithBaseList(: IDataReader)`. *Synto could* parameterize the emitted shell,
   distinct from the body. (Task 4)
4. **A `[TemplateHole]` / reserved-member marker.** Because invariant members call the variable ones, the template
   carrier must include compiling placeholders for members it intends to replace. A marker that reserves a member
   to be supplied at quote time would let the skeleton type-check without a dummy body. (Task 4)
5. **An interceptor-stub helper.** Emitting `[InterceptsLocation]` + the self-contained
   `InterceptsLocationAttribute` definition, plus the generic-arity `(object)` double-cast to bridge `Create<T>`
   to the concrete reader, is pure plumbing. *Synto could* emit an interceptor stub from an interceptable-location
   + a target type. (Task 2, R1/R3)

Lower-impact, documentation-only: consuming `Synto.SourceGenerator` already provides `IsExternalInit` (the
hand-rolled polyfill becomes a CS0101 collision — delete it); and dog-fooding `Synto.Diagnostics` inside a
*generator* (not an analyzer) yields a benign RS2002 from release-tracking (the descriptors aren't any analyzer's
`SupportedDiagnostics`).

---

## Task 2 — walking-skeleton generator (raw SyntaxFactory)

- **Hand-rolled `EquatableArray<T>` + `IsExternalInit` polyfill (cacheability boilerplate).** A cacheable
  incremental generator needs value-equatable pipeline state, so I re-authored `EquatableArray<T>` and, because
  the generator TFM is netstandard2.0, an `IsExternalInit` polyfill so `record struct` `init` accessors compile.
  Synto already owns both internally but exposes neither to a generator author. *Synto could make this easier by*
  shipping a tiny generator-author "cacheability toolkit" (an injectable `EquatableArray<T>` + the polyfill),
  the same way the consumer surface is injected.

- **R1 — generic interceptor binding forces an `(object)` double-cast.** The intercepted `Create<T>` is generic,
  so the interceptor must share its arity (`Create_N<T>`), but the specialized reader is typed to the concrete
  target. Bridging `IEnumerable<T>` to `IEnumerable<Person>` is illegal directly (CS0030), so the body casts via
  `(IEnumerable<Person>)(object)source`. Works (T is always the target at the call site) but is interceptor tax.
  *Synto could make this easier by* offering a templating helper that emits an interceptor stub from an
  interceptable-location + a target type, hiding the arity/cast ceremony.

- **R3 — the generator must emit `InterceptsLocationAttribute` itself.** `GetInterceptsLocationAttributeSyntax()`
  emits the *usage*; the SDK ships no `System.Runtime.CompilerServices.InterceptsLocationAttribute`, so the
  generator also emits the (self-contained, BCL-only) attribute definition. Pure plumbing the consumer never sees.

- **Type-vs-namespace collision in the locked names.** `ObjectReader` (the API class) collides with the
  `Synto.Example.ObjectReader` namespace; inside any `Synto.Example.ObjectReader.*` file the bare `ObjectReader`
  binds to the namespace, so every same-root caller (Demo, Tests) needs a `using ObjectReader = global::…Api.ObjectReader;`
  alias. Example-internal naming, not Synto's fault — noted for the eventual naming review.

- **Source-gen specialization can't target a `private` nested type.** The emitted reader lives in its own file
  and names the target by fully-qualified name, so the behavioral test's target must be at least `internal`
  (a `private` nested record is unreachable from the generated reader — CS0122). General source-gen constraint.

## Task 4 — dog-food: migrate the reader skeleton onto a Synto `[Template]`

- **What templated cleanly (the win).** The whole INVARIANT `IDataReader` surface — the `_e` enumerator field +
  constructor, `GetValues`/`IsDBNull`/both indexers, the 13 typed getters, `GetDataTypeName`, `Depth`/`IsClosed`/
  `RecordsAffected`, `Read`/`NextResult`/`Close`/`Dispose`, the unsupported `GetSchemaTable`/`GetBytes`/`GetChars`/
  `GetData`, and the `OutOfRange`/`NoColumn` helpers — is authored ONCE as real C# in `ReaderTemplate.cs` and quoted
  by a single `[Template(typeof(Factory))]` class. The element type flows in as a syntax hole via
  `[Inline(AsSyntax = true)] T` and splices wherever `T` appears. `Factory.ObjectReaderTemplate(elementType)` returns
  the `ClassDeclarationSyntax`. The output is byte-identical to the Task 2 raw-`SyntaxFactory` golden (the snapshot
  re-verified with no change), so the migration is behavior-preserving.

- **A→B drop (R2): list-driven members are not expressible as a fixed `[Template]`.** `FieldCount` and the four
  switches (`GetName`/`GetOrdinal`/`GetFieldType`/`GetValue`) have a **variable arm count** — one arm per resolved
  column. A `[Template]` captures a fixed syntax shape, and a Synto hole (`[Inline]` / `Syntax<>`) splices a SINGLE
  node, not a data-driven *list expansion*. So these five members stayed raw `SyntaxFactory` and are swapped in over
  compiling placeholders in the quoted skeleton. *Synto could make this easier by* a **list/repeater hole** that maps
  a collection to a `SeparatedList` of arms (a data-driven cousin of the `Syntax statement; statement();` repeater in
  the templating examples), or a `Pattern.Replace`-style splice keyed on a placeholder arm.

- **The template carrier must itself compile — including the members it intends to replace.** Because the invariant
  members reference the variable ones (`GetValues`→`GetValue`, every typed getter→`GetValue`, `this[string]`→
  `GetOrdinal`), the skeleton can't simply omit them; each must exist as a compiling placeholder body that the
  generator overwrites. *Synto could make this easier by* a `[TemplateHole]`/abstract-member marker that reserves a
  member to be supplied at quote time, so the skeleton type-checks without a dummy body.

- **Post-quote fix-ups are hand-authored raw `SyntaxFactory`.** A `[Template]` can't carry a `file` modifier (file
  types are top-level only) nor a per-call-site type name, so the generator still does `.WithIdentifier(...)` (rename
  the class AND its constructor), `.WithModifiers(file sealed)`, and `.WithBaseList(: IDataReader)` after
  `Factory.ObjectReaderTemplate(...)`. *Synto could make this easier by* template parameters for the emitted type's
  name / modifiers / base-list (the variable *shell*, distinct from the variable *body*).

- **Adopting `Synto.SourceGenerator` turned the Task 2 hand-rolled `IsExternalInit` into a COLLISION.** The injected
  surface ships its own `System.Runtime.CompilerServices.IsExternalInit`, so the polyfill a netstandard2.0 generator
  needs *before* consuming Synto had to be deleted *after* (CS0101 duplicate). Net boilerplate reduction, but a sharp
  edge worth a doc note: "consuming the generator already provides `IsExternalInit`." (The hand-rolled
  `EquatableArray`/`DiagnosticInfo`/`LocationInfo` from Tasks 2–3 are NOT injected and remain — see the Task 2 entry.)

- **RS2002, the inverse of the anticipated RS2008.** Dog-fooding `Synto.Diagnostics` *inside a source generator*
  (rather than a `DiagnosticAnalyzer`) means the `SOR0000/0001/0002` descriptors are listed in
  `AnalyzerReleases.Unshipped.md` but are not any analyzer's `SupportedDiagnostics`, so the release-tracking analyzer
  warns RS2002 ("not a supported diagnostic for any analyzer"). Benign (a warning, not a gate failure); an artifact of
  emitting diagnostics from a generator. Left un-suppressed as honest friction.

- **Interceptor plumbing (R1/R3) stayed raw `SyntaxFactory`.** The `ObjectReaderInterceptors` holder and the
  `[InterceptsLocation]`/attribute-definition emission are deliberately NOT migrated — they're not the dog-food target
  and the generic-arity + `(object)` double-cast tax is already logged under Task 2 (R1) and the self-emitted
  `InterceptsLocationAttribute` under Task 2 (R3).
