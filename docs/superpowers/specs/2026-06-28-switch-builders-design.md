# Switch builders — list-shaped `[SyntaxBuilder]` helpers (user-facing design)

**Date:** 2026-06-28
**Status:** Design (user-facing surface). Data-driven `switch` statement + expression scoped to
ship first via the existing `[SyntaxBuilder]` mechanism; richer patterns (type/property) and the
implicit if-chain→switch peephole designed-out / deferred.

## Context

Synto templates are "write real C#, it gets quoted." Control flow that *drives over a live value*
(`foreach`/`for`/`while`/`if` over a `Parameter<>()`/`Unquote()` root) is **unrolled** at
factory-build time into a `List<StatementSyntax>` (`BindingTimeClassifier` →
`StagedRegionEmitter`). Everything else is quoted verbatim.

`switch` falls outside that. There is **no classifier case and no emitter arm for
`SwitchStatementSyntax`/`SwitchExpressionSyntax`** today, and no test that even a *fixed* switch
round-trips through the quoter. More importantly, the common need is a **data-driven** switch —
one `case`/arm per item in a staged collection (the ObjectReader typed-getter shape:
`switch (i) { case <ordinal>: … }`). That one **cannot be authored as literal C#**: the template
body must compile, and C# has no way to loop-generate `case` labels or switch-expression arms
(`switch (i) { foreach(…) case …: }` is not valid C#). The `foreach`-unroll trick that powers
everything else can't reach it — switch sections are not statements and a `foreach` cannot live
inside a switch body.

So for data-driven switches, dropping to `SyntaxFactory` feels jarring but **some builder is
unavoidable**. The only real question is how Synto-native we can make it — and the answer is that
the mechanism already exists.

## The mechanism already exists: `[SyntaxBuilder]`

A static method marked `[SyntaxBuilder]` is auto-discovered by the generator
(`SyntaxBuilderFinder.FindBuilders`), its caller-facing **facade is synthesized**, and its
arguments are bound **declaratively** from `[Quoted]` markers (`FacadeShape.Derive`). Unmarked
parameters are *staged* (a real factory-build-time value); `[Quoted]` parameters quote the
author's expression into a syntax island; the method returns a Roslyn node that the generator
splices in. **No generator change is required to add a builder** — this is the documented
extensibility path, distinct from the hard-coded built-ins (`Member`/`TypeOf`).

What is missing is not infrastructure but a **worked example of `[SyntaxBuilder]` for a complex,
list-shaped construct**. Switch is that example, and it establishes the reusable recipe.

## The reusable pattern: list-shaped builders

Most "complex helpers" share one shape: a **host node with a data-driven child list** that is not
a plain statement list (so the existing unroll can't reach it). Switch is one; the identical recipe
covers `try`/N-catch (`List<CatchClauseSyntax>`), object/collection initializers
(`SeparatedList<ExpressionSyntax>`), tuple patterns, attribute lists, and **interpolated strings**
(`InterpolatedStringExpression` = a list of text + hole parts).

The recipe is a pair of `[SyntaxBuilder]` methods:

1. **Host builder** — takes the fixed parts + the child node list, returns the host node.
2. **Element builders** — produce the list items from a (possibly templated) body.

The element list is authored with **ordinary LINQ/`foreach` over the live collection** (it runs at
factory-build time), optionally appending a fixed element (e.g. a literal `default`). Element
**bodies are orthogonal** to the builders — a body is just syntax, so it can come from a trivial
builder (`Return(Member<…>(…))`) or from a child `[Template]` for arbitrary real-C# bodies.

Future complex helpers follow the identical recipe: add a host builder + element builders, reuse the
LINQ-over-live-collection idiom. No new generator concepts per helper.

## User-facing surface (switch, the first client)

Shipped in the `Synto` runtime alongside `SyntoBuilders`, so consumers get them out of the box; they
also stand as the canonical worked example of the pattern for consumers' own helpers.

### Single overloaded `Switch` — statement vs expression chosen by element type

```csharp
[SyntaxBuilder] SwitchStatementSyntax  Switch([Quoted] ExpressionSyntax governing,
                                              params IEnumerable<SwitchSectionSyntax> sections);

[SyntaxBuilder] SwitchExpressionSyntax Switch([Quoted] ExpressionSyntax governing,
                                              params IEnumerable<SwitchExpressionArmSyntax> arms);
```

C# overload resolution disambiguates on the list element type — the author never picks a name, the
body shape (`Case` vs `Arm`) decides:

```csharp
Switch(i, cols.Where(...).Select(c => Case(c.Ordinal, Return(Member<TRet>(_e.Current, c.Name)))))
Switch(i, cols.Where(...).Select(c => Arm(c.Ordinal,  Member<TRet>(_e.Current, c.Name))))
```

`governing` is `[Quoted]`: the author writes `i` and it is quoted to `IdentifierName("i")`. The
section/arm list is *staged* (unmarked): a factory-build-time value produced by the LINQ projection.

`params IEnumerable<T>` (C# 13 params collections) gives both call shapes with no `.ToArray()`:
- pass the projection directly as the single params argument (normal form, no copy), or
- pass individual elements `Switch(i, Case(...), Case(...), Default(...))` (expanded form).

### Element builders

```csharp
// statement sections
[SyntaxBuilder] SwitchSectionSyntax Case([Quoted] ExpressionSyntax label,
                                         params IEnumerable<StatementSyntax> body);
[SyntaxBuilder] SwitchSectionSyntax Default(params IEnumerable<StatementSyntax> body);

// expression arms
[SyntaxBuilder] SwitchExpressionArmSyntax Arm([Quoted] ExpressionSyntax patternConstant,
                                              [Quoted] ExpressionSyntax value);
[SyntaxBuilder] SwitchExpressionArmSyntax WhenArm([Quoted] ExpressionSyntax patternConstant,
                                                  [Quoted] ExpressionSyntax guard,
                                                  [Quoted] ExpressionSyntax value);
[SyntaxBuilder] SwitchExpressionArmSyntax DefaultArm([Quoted] ExpressionSyntax value); // the `_ =>` discard arm
```

Plus one tiny statement builder so the common case needs no child template:

```csharp
[SyntaxBuilder] StatementSyntax Return([Quoted] ExpressionSyntax value);
```

(Exact signatures of label/pattern parameters are the implementer's to finalize during the spike;
the contract is "constant label/pattern quoted from the author's expression, body/value as syntax".)

## Empirical gate (residual unknowns — spike first)

The `[SyntaxBuilder]` mechanism is proven for simple builders, but three things on this design are
**unproven** and gate the plan. Task 1 is a standalone composition test that exercises them in
isolation before ObjectReader is touched. If it fails *at generation time* (not merely a
missing-snapshot Verify fail) → systematic-debugging; do not silently widen scope.

1. **LINQ projection into a staged builder argument.** `cols.Where(...).Select(c => Case(c.Ordinal,
   …))` must evaluate at factory-build time into `IEnumerable<SwitchSectionSyntax>` fed to a host
   builder — i.e. a `[SyntaxBuilder]` call (`Case`) inside a live `.Select` lambda. `foreach`/`.Where`
   over a live collection is proven; *builder-call-inside-a-live-projection-as-a-builder-arg* is not.
2. **Overloaded `[SyntaxBuilder]` discovery.** Two `Switch` overloads differing only by element type.
   Recognition binds to the resolved `IMethodSymbol`, so it *should* hold, but it is untested.
3. **`params IEnumerable<T>` facade synthesis.** The synthesized facade must preserve the `params`
   collection. If it flattens to a plain `IEnumerable<T>`, we lose only the comma-separated form and
   keep the projection form — a graceful degrade, not a redesign.

## Dog-food target

After the child-templates narrow case lands (it proves "a template invoking a template" and adds the
`GetterTemplate.TypedGetter` child), refactor the typed getter body from
`foreach + if (i == c.Ordinal) return …` into the switch builder (`Switch(i, …)` /
switch-expression form), behavior-preserving. The ObjectReader snapshot is the gate; this validates
the feature on real code and mines getter friction per the dog-food principle. Sequenced **after**
the in-flight child-templates work.

## Cheap side-task: verbatim switch round-trip

No test today proves a hand-written **fixed** `switch` statement *or* switch expression quotes
verbatim through a template. Add that coverage. Pure confirmation; no new surface. (If the quoter
turns out *not* to round-trip a fixed switch, that is its own bug to file — not part of the builder
work.)

## Scope & phasing

- **Ship first:** data-driven `switch` **statement** (`Switch`/`Case`/`Default`) and **expression**
  (`Switch`/`Arm`/`WhenArm`/`DefaultArm`), plus `Return`, all as runtime `[SyntaxBuilder]` methods;
  the verbatim round-trip test; the ObjectReader getter dog-food.
- **v1 pattern coverage:** constant-label cases + `default` (statements); constant/`_` arms + `when`
  guards (expressions). Type patterns / property patterns are additional `[Quoted(AsTypeArg = true)]`
  / pattern-builder overloads added later — **not v1**.
- **Designed-out:** the implicit if-chain→switch **peephole** (recognize `foreach + if (x == const)`
  and emit a switch). Zero authoring surface but magic, narrow (constant dispatch only), no rich
  patterns, no switch-expression story, and deep generator work. Explicitly out of scope.

## What it can NOT do (limits)

- **Data-driven switches are never literal C#.** The "write real C#" idiom only ever covers *fixed*
  switches; the data-driven case is always a builder call. This is a C# language constraint, not a
  Synto gap.
- **Syntax-only, cacheable inputs.** Builder arguments are equatable syntax/data (`ExpressionSyntax`,
  `TypeSyntax`, primitives, `EquatableArray<…>`) — no `ISymbol`/`SemanticModel` — so switch
  construction stays inside the incremental-generator cacheability rules. Nodes in, nodes out.
- **Trivia/formatting.** Built nodes must normalize byte-identically after `dotnet format whitespace`
  to satisfy snapshot gates; builders should emit minimally-trivia'd nodes and rely on the formatter.

## Relationship to child-templates and the interpolated-string gap

This is a sibling of the child-templates work, not a dependency: child-templates supplies arbitrary
real-C# **bodies** for cases/arms, but the switch builders also work with trivial builder bodies
(`Return(Member<…>(…))`), so neither blocks the other.

The child-templates ObjectReader collapse surfaced a closely-related gap: a **staged constant string
will not fold into interpolated-string literal text** — Synto renders it as an interpolation hole
(`$"… {"a Boolean"} …"`) rather than merged text (`$"… a Boolean …"`). An interpolated string is
itself a list-shaped construct, so the same pattern (an `Interp(...)` builder) or a small quoter
enhancement is the natural home for that fix. It is **out of scope here** but recorded as the next
list-shaped-builder candidate, and as evidence the pattern generalizes.

## Open decisions

- **Final label/pattern parameter shape** for `Case`/`Arm`/`WhenArm` — settled during the Task 1
  spike against what the synthesized facade accepts.
- **Whether `Return` / the trivial body builders** live next to the switch builders or in a shared
  statement-builder file — a packaging detail, decided at implementation.
