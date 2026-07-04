---
type: Reference
title: Glossary
description: Core Synto vocabulary, each term pointing at its single canonical defining file.
tags: [meta, glossary]
timestamp: 2026-07-04T00:00:00Z
---

Synto's vocabulary is overloaded and load-bearing. Each term below links to the
one file that defines it; follow into the relevant [subsystem](/subsystems/templating.md)
for the full picture.

| Term | Canonical file | One-liner |
|------|----------------|-----------|
| **Quote** | `src/Synto/Quoting/CSharpSyntaxQuoter.cs:21` | Turn a syntax tree into the C# expression that rebuilds it. See [quoting](/subsystems/quoting.md). |
| **Template** | `src/Synto/Templating/Definition/TemplateAttribute.cs:23` | `[Template<Target>]` on a method/class/struct; generator completes it into a `public static` factory on `Target`. Requires C# 11 (generic attribute). See [templating](/subsystems/templating.md). |
| **Sub-template** | `src/Synto/Templating/Definition/TemplateAttribute.cs:11` | A **nested** parameterless `[Template]` inside a `[Template<T>]` carrier; the parent builds it into its own single document as an `internal static` method (not an independent sibling). A nested `[Template<T>]` is illegal → **SY1034**; a top-level bare `[Template]` → **SY1033**. See [templating](/subsystems/templating.md). |
| **Staged / binding-time** | `src/Synto.SourceGenerator/Templating/Analysis/BindingTimeClassifier.cs` | Split of a template body into `Quoted` (emitted as output syntax) vs live (`Unquote`/`Parameter`, computed at factory-build time; control flow over live data is unrolled). |
| **Unquote** | `src/Synto/Templating/Staging/UnquoteAttribute.cs` | Value/type supplied at invocation, lifted into syntax at factory time; **may drive** staged control. Inline form: `Template.Unquote<T>(expr)`. |
| **Quote (param)** | `src/Synto/Templating/Definition/QuoteAttribute.cs` | Like Unquote but **never** drives control — the enclosing loop/condition stays a runtime construct. Inline form: `Template.Quote<T>(value)`. |
| **Splice** | `src/Synto/Templating/Staging/SpliceAttribute.cs` | Pre-built `ExpressionSyntax`/`TypeSyntax` inserted verbatim (no evaluation); also marks member-generator methods. Inline form: `Template.Splice(node)`. |
| **Parameter (live)** | `src/Synto/Templating/Staging/Template.Parameter.cs:22` | `Template.Parameter<T>()` — a live value carrier; the bound variable name becomes the factory parameter name. |
| **SyntaxBuilder** | `src/Synto/Templating/Builders/SyntaxBuilderAttribute.cs` | A factory-time builder method; generator synthesizes an inert facade so a template body can call it by name. |
| **Decoration** | `src/Synto/Templating/Decorations/IdentifierAttribute.cs:6` | Post-quote marker that modifies a quoted declaration (identifier/visibility/sealed/implements/inherits). See [decorations](/subsystems/decorations.md). |
| **Matching** | `src/Synto/Matching/MatchAttribute.cs:11` | `[Match<TMatcher>]` pattern DSL → generated matcher + `Replace`/`ForMatch` APIs. See [matching](/subsystems/matching.md). |
| **Capture** | `src/Synto/Matching/CaptureAttribute.cs:10` | Hole in a match pattern: `[Capture]` binds any `ExpressionSyntax`; `[Capture<TNode>]` narrows. |
| **Bootstrap quoter** | `src/Synto.Bootstrap/CSharpSyntaxQuoter.cs:22` | The build-time quoter that quotes itself to emit the runtime quoter. See [bootstrap](/architecture/bootstrap.md). |

> Naming note: the **Live/Inline → Unquote/Splice** rename shipped 2026-06-27
> (`Live`/`Inline`/`AsSyntax` deleted); this glossary uses the current names.
> Older specs may still say "Live".
