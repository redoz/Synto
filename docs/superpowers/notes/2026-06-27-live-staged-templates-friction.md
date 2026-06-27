# Live (staged) templates — friction log

Genuine findings as the live-staged-templates feature is implemented (seam strain, near-misses on the quoter,
builder/usings sharp edges, deliberately deferred scope). Empty findings are not manufactured.

## Deferred / out-of-cut (logged, not stubbed)

- **`SeparatedSyntaxList<TNode>` collection helper (Task 5 scope note).** Only the non-separated
  `SyntaxList<TNode>` builder (`CollectionSyntaxExtensions.BuildList`) ships in v1. The in-scope ObjectReader
  dog-food reshapes its `switch` into an `if`-chain, which stays in non-separated **statement** lists, so the
  separator-interleaving variant is never exercised. Deferred deliberately; revisit if a later live region needs
  to emit a comma/separator-delimited list (arguments, parameters, initializer elements) directly from an
  unrolled run.

## Builder mechanism (Task 3)

- **`[Inline(AsSyntax = true)]` in PARAMETER-TYPE position** (`Build<[Inline(AsSyntax=true)] T>(T instance)`) emits
  `Parameter(..., <ExpressionSyntax>, ...)` into a `TypeSyntax` slot → CS1503. The plan's `Member` snapshot uses
  that form but only snapshots it (latent, never compiled); the zero-collision compile test uses the param form
  `[Inline(AsSyntax = true)] object instance` instead. Quoter/bootstrap untouched.
