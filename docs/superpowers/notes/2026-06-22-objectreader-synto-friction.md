# ObjectReader → Synto friction log

Living log of where building the ObjectReader dog-food example against Synto-as-is hurt.
Feeds a separate Synto-improvement spec (spec §11). One entry per finding: what hurt, why,
"Synto could make this easier by …".

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
