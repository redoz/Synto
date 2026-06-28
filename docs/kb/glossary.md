---
type: Reference
title: Glossary
description: Core Synto vocabulary, each term pointing at its single canonical defining file.
tags: [meta, glossary]
timestamp: 2026-06-28T00:00:00Z
---

Synto's vocabulary is overloaded and load-bearing. Each term below links to the
one file that defines it; follow into the relevant [subsystem](/subsystems/templating.md)
for the full picture.

| Term | Canonical file | One-liner |
|------|----------------|-----------|
| **Quote** | `src/Synto/CSharpSyntaxQuoter.cs:21` | Turn a syntax tree into the C# expression that rebuilds it. See [quoting](/subsystems/quoting.md). |
| **Template** | `src/Synto/Templating/TemplateAttribute.cs:6` | `[Template(typeof(Target))]` on a method/class/struct; generator completes it into a factory. See [templating](/subsystems/templating.md). |
| **Staged / binding-time** | `src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs` | Split of a template body into `Quoted` (emitted as output syntax) vs live (`Unquote`/`Parameter`, computed at factory-build time; control flow over live data is unrolled). |
| **Unquote** | `src/Synto/Templating/UnquoteAttribute.cs` | Value/type supplied at invocation, lifted into syntax at factory time; **may drive** staged control. Inline form: `Template.Unquote<T>(expr)`. |
| **Quote (param)** | `src/Synto/Templating/QuoteAttribute.cs` | Like Unquote but **never** drives control — the enclosing loop/condition stays a runtime construct. Inline form: `Template.Quote<T>(value)`. |
| **Splice** | `src/Synto/Templating/SpliceAttribute.cs` | Pre-built `ExpressionSyntax`/`TypeSyntax` inserted verbatim (no evaluation); also marks member-generator methods. Inline form: `Template.Splice(node)`. |
| **Parameter (live)** | `src/Synto/Templating/Template.Parameter.cs:22` | `Template.Parameter<T>()` — a live value carrier; the bound variable name becomes the factory parameter name. |
| **SyntaxBuilder** | `src/Synto/Templating/SyntaxBuilderAttribute.cs` | A factory-time builder method; generator synthesizes an inert facade so a template body can call it by name. |
| **Decoration** | `src/Synto/Templating/IdentifierAttribute.cs:6` | Post-quote marker that modifies a quoted declaration (identifier/visibility/sealed/implements/inherits). See [decorations](/subsystems/decorations.md). |
| **Matching** | `src/Synto/Matching/MatchAttribute.cs:11` | `[Match<TMatcher>]` pattern DSL → generated matcher + `Replace`/`ForMatch` APIs. See [matching](/subsystems/matching.md). |
| **Capture** | `src/Synto/Matching/CaptureAttribute.cs:10` | Hole in a match pattern: `[Capture]` binds any `ExpressionSyntax`; `[Capture<TNode>]` narrows. |
| **Bootstrap quoter** | `src/Synto.Bootstrap/CSharpSyntaxQuoter.cs:22` | The build-time quoter that quotes itself to emit the runtime quoter. See [bootstrap](/architecture/bootstrap.md). |

> Naming note: a mechanical rename of **Live/Inline → Unquote/Splice** is in
> flight; this glossary uses the target names. Older specs may still say "Live".
