# Builder overload resolution — argument-directed, bounded, diagnostic-on-ambiguity

**Status:** Design (2026-07-11, accepted). Refines the getter-expression-builders plan's Task 2
(`2026-07-11-getter-expression-builders.md`) and supersedes the resolver mechanism sketched in
`2026-07-11-getter-expression-builders-design.md` §2.2. Written after an adversarial pre-implementation
review surfaced a load-bearing hole in the naive resolver (see §4).

**Origin.** Today `FacadeCallFinder` binds a same-name group of `[SyntaxBuilder]`s only when they all
share a binding shape (`ShareBindingShape` — identical quoted/staged/type-arg parameter-kind sequence);
any group with **differing** shapes is rejected wholesale with **SY1018**. That blanket rejection blocks
the ordinal `Arm` overload: staged `Arm(ExpressionSyntax pattern, [Quoted] value)` and ordinal
`Arm([Constant] SyntaxNode ordinal, [Quoted] value)` differ in kind (`Staged` vs `Constant`) at param 0,
so they trip SY1018 even though a human reads `Arm(2, v)` vs `Arm(somePattern, v)` unambiguously.

This spec defines a **small, reliable** argument-directed resolver that lets such overloads coexist —
and, crucially, defines what happens when the resolver **cannot** decide: it emits an actionable
diagnostic, never an arbitrary pick.

## 1. Design principle — bounded, not C#

We do **not** replicate C# overload resolution. We implement the least mechanism that disambiguates the
overloads Synto's own surface needs (ordinal vs staged `Arm`) and rejects everything it can't decide
reliably. Guiding rules:

- **Only `[Staged]` parameters discriminate.** A staged param faces a real builder-typed facade parameter
  (e.g. `ExpressionSyntax`), so the call argument's type is a genuine signal.
- **`[Quoted]` / `[Constant]` parameters never discriminate and are never type-checked at resolution
  time.** They are *islands*: the facade types them as `object` (or `As`) and accepts ANY expression by
  design. Type-checking them is both unnecessary and unsafe (§4).
- **`[TypeArg]` parameters are not value arguments** and are skipped (arity is already narrowed upstream by
  `NarrowByTypeArgArity`).
- **When the rule leaves a tie, we stop and diagnose** (§5) — we do not invent a winner.

## 2. Algorithm

Runs inside `FacadeCallFinder.VisitInvocationExpression`, only on the existing
`resolved.Count > 1 && !ShareBindingShape(resolved)` branch (shape-*sharing* groups — the two `Switch`
overloads — keep their current path untouched and never reach here).

**Step A — applicability.** A candidate is *applicable* to the call iff:

1. its facade **value-parameter count** (parameters minus `[TypeArg]`) equals the call's argument count; and
2. for every parameter, in order:
   - **`[Quoted]` / `[Constant]` (object-facing):** always compatible. Not type-checked. (This is the §4
     fix — the argument may be a nested, still-unbound facade call whose type is unresolvable.)
   - **`[Staged]`:** the argument's resolved type must exist **and** have an implicit conversion to the
     builder parameter type. If the argument type cannot be resolved (null / error type), the candidate is
     **not applicable** — a staged position requires a real, bound factory-time expression.

**Step B — specificity.** Among applicable candidates, "more specific" = **fewer object-facing params**
(equivalently: more staged params pinned by real types). Compute the minimum object-facing-param count;
keep the candidates at that minimum.

**Step C — decide.**
- Exactly one candidate at the minimum → bind it.
- Zero applicable, or a **tie** at the minimum → emit the ambiguity diagnostic (§5) and quote the call
  verbatim (no builder rewrite), mirroring today's SY1018 bail.

### Worked cases (the `Arm` pair)

Facades: staged `Arm(ExpressionSyntax /*staged*/, object /*quoted*/)` — 1 object-facing;
ordinal `Arm(object /*constant*/, object /*quoted*/)` — 2 object-facing.

| Call | staged applicable? | ordinal applicable? | winner |
|------|--------------------|---------------------|--------|
| `Arm(2, v)` | no (`int` ↛ `ExpressionSyntax`) | yes | ordinal |
| `Arm(somePattern, v)` where `somePattern : ExpressionSyntax` | yes | yes | staged (1 < 2 object-facing) |
| `Arm(Member<object>(x,"P"), v)` (nested facade as pattern) | no (arg type unresolvable at a **staged** slot) | ordinal's `[Constant]` would then fail its own lift → bind error | diagnosed, not silently mis-bound |

The second arg (`v`, the `[Quoted]` value) is **object-facing in both** overloads, so it never enters the
decision — even when it is itself a nested `Member<…>(…)` / `Coalesce(…)` island (the real dog-food usage).

## 3. Public surface / interfaces

- `FacadeCallFinder` gains a private
  `ResolveOverloadByArguments(List<IMethodSymbol> candidates, InvocationExpressionSyntax invocation) : IMethodSymbol?`
  returning the single chosen overload, or `null` (→ caller emits the ambiguity diagnostic).
- A private `FacadeValueParamTypes(IMethodSymbol builder, ITypeSymbol objectType) : List<ITypeSymbol>?`
  mapping builder parameters to their facade value-parameter types **in call order**: `[Staged]` → builder
  param type; `[Quoted]` → the `As` type if given else `object`; `[Constant]` → `object`; `[TypeArg]` →
  skipped. Returns `null` when the builder does not derive a valid facade.
  - *Note:* honor `[Quoted(As = …)]` here — do not hardcode `object` for quoted — so the resolver's view of
    a facade parameter matches what `FacadeShape` actually synthesizes.
- Nothing is captured into incremental-pipeline state (the resolver reads the `SemanticModel` and returns a
  symbol locally). Cacheability asserts stay green.

## 4. Why object-facing args must NOT be type-checked (the review finding)

A `[Quoted]` / `[Constant]` argument is very often a **nested facade call** — `Member<TRet>(_e.Current, c.Name)`,
`Coalesce(…, Throw<object>(…))`. Facade methods are **not present in the compilation the generator analyzes**
(recognition is structural precisely because a genuine facade call is UNBOUND — see `FacadeCallFinder`
class doc). So `GetTypeInfo(nestedFacadeCall).Type` is null or an error type.

A naive resolver that calls `GetTypeInfo` + `ClassifyConversion` on **every** argument therefore rejects the
candidate at that object-facing slot (or relies on Roslyn's undefined error-type conversion behavior),
yielding a **spurious SY1018 on the real dog-food call** — the feature's whole reason to exist. Today this
is masked only because a single-overload `Arm` skips the resolver entirely and the binder never types
`[Quoted]` args (`FacadeArgumentBinder` types `[Constant]` only).

**Resolution:** object-facing params are unconditionally applicable at resolution time. Their late binding
is unchanged: `[Quoted]` islands re-enter the builder pass; `[Constant]` values lift via `ToSyntax` at the
binding seam (where an unresolvable constant type is a separate, already-existing bind error).

## 5. Diagnostic on unresolvable ambiguity

When Step C finds zero applicable candidates or a tie, reuse **SY1018** (`AmbiguousBuilder`) — it already
means "can't pick a builder overload" — but upgrade its message to be **actionable**. It must name the
call and offer concrete escapes:

> **SY1018:** Cannot determine which `Arm` overload the call `Arm(<args>)` targets — the candidates differ
> in binding shape and the arguments don't distinguish them. Fix by one of: (a) give the overloads a
> distinguishing `[Staged]` parameter whose type the argument selects; (b) rename one overload so the call
> is unambiguous; (c) call with an argument whose type matches exactly one overload's staged parameter.

The message lists the candidate signatures. We deliberately do **not** attempt heroic resolution — a
predictable error with a fix-it beats a clever guess. "We don't support everything C# does" is the policy:
if we can't decide reliably, we say so.

## 6. Contracts & invariants (binding on plan)

- **Ordinal `Arm` resolves, staged `Arm` unregressed.** `Arm(2, v)` binds the ordinal overload;
  `Arm(<built ExpressionSyntax pattern>, v)` still binds the staged overload. Both green.
- **Nested-facade `[Quoted]` value arg is never type-checked at resolution.** `Arm(2, Member<object>(x,"P"))`
  and `Arm(c.Ordinal, Coalesce(Member<object>(…), Throw<object>(…)))` resolve to ordinal `Arm` without
  SY1018. **This is the case the resolver's own test must cover** — not only fully-bound arguments.
- **Shape-sharing groups untouched.** The two `Switch` overloads (both `[Quoted governing, Staged params]`)
  still bind via `resolved[0]` and never enter the resolver. No regression.
- **Genuine ambiguity still errors.** Two non-shared-shape overloads with equal specificity for the call
  (e.g. `Two([Quoted] ExpressionSyntax)` vs `Two([Constant] SyntaxNode)` called `Two(null)`) report SY1018
  with a real span and the upgraded message.
- **Cacheability preserved.** No `ISymbol` / `SemanticModel` / `SyntaxNode` enters cached pipeline state.
- **Bounded scope is a feature.** `params` non-shared-shape mixes and other exotic overload sets are NOT
  specially supported; if they don't resolve under §2 they diagnose under §5.

## 7. Test matrix

Must exist before the ordinal-`Arm` overload (its consumer) lands:

1. **Bound-arg resolution** — `Pick(42, v)` → constant overload; `Pick(LiteralExpression(…), v)` → staged
   overload; no SY1018. (Baseline; the naive version passes this — insufficient alone.)
2. **Nested-unbound-facade `[Quoted]` arg** — `Pick(42, Member<object>(v, "X"))` → constant overload, no
   SY1018. **This is the regression guard for §4** and the one the naive resolver fails.
3. **Genuine ambiguity** — equal-specificity non-shared-shape overloads called with a non-distinguishing
   argument → single SY1018 with a real span and the actionable message.
4. **Shape-sharing untouched** — `Switch`/`Switch<T>` overload tests stay green.
5. **Cacheability** — template-factory cacheability asserts stay green.

## 8. Scope & stop-condition

In scope: the §2 resolver + §5 diagnostic upgrade, sufficient for the ordinal `[Constant]` `Arm` overload.
Out of scope: general C#-fidelity overload resolution, `params` disambiguation, `As`-typed quoted
overload sets beyond honoring the type in `FacadeValueParamTypes`.

**Stop-if-ugly.** `[Constant]` ordinal ergonomics are the prize, but not at any cost. If the resolver or its
diagnostics balloon past the §2 algorithm, fall back to the no-surgery alternative — a standalone
`Constant(value)` literal builder feeding the **existing** single staged `Arm(Constant(c.Ordinal), value)` —
which needs no resolver at all. That path keeps the `[Constant]` value-lift ergonomics while dropping the
exact `Arm(c.Ordinal, …)` surface. Prefer the resolver; abandon it cleanly if it stops being small.

## 9. Open questions (carried to plan)

- **SY1018 reuse vs new id.** Spec assumes reuse with an upgraded message. If the "genuinely unresolved
  shape conflict" and "arg-undistinguished overloads" cases want distinct ids for consumers, split then —
  but one actionable SY1018 is the default.
- **Message rendering of candidate signatures.** Confirm the diagnostic-args plumbing can carry a
  candidate-list string without leaking symbols into equatable diagnostic state.
