---
type: Subsystem
title: Decorations
description: Post-quote declaration markers that modify a quoted declaration's identifier, visibility, sealed-ness, and base types.
resource: src/Synto/Templating
project: projects/synto-runtime
entrypoints: ["src/Synto/Templating/Decorations/IdentifierAttribute.cs:6"]
tags: [decorations, declarations, templating]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

A small, newest-built surface: markers applied to a quoted declaration that
**decorate** it after quoting — rename it, set its access, seal it, or add base
types. Each attribute pairs with an `Apply…AttributeExtensions` method that
performs the post-quote rewrite.

# Key files

| Marker | Attribute | Extension | Purpose |
|--------|-----------|-----------|---------|
| Identifier | `Templating/Decorations/IdentifierAttribute.cs:6` | `Decorations/IdentifierAttributeExtensions.cs:8` | Rename declaration (+ its constructors). |
| Visibility | `Templating/Decorations/VisibilityAttribute.cs:6` | `Decorations/VisibilityAttributeExtensions.cs:11` | Set access modifiers (via `Access`). |
| Sealed | `Templating/Decorations/SealedAttribute.cs:6` | `Decorations/SealedAttributeExtensions.cs:9` | Add the `sealed` keyword. |
| Implements | `Templating/Decorations/ImplementsAttribute.cs:6` | `Decorations/ImplementsAttributeExtensions.cs:8` | Add an interface base type (repeatable). |
| Inherits | `Templating/Decorations/InheritsAttribute.cs:6` | `Decorations/InheritsAttributeExtensions.cs:8` | Add a base class (prepended). |

- `Templating/Decorations/Access.cs:3` — the `Access` enum: `Public`, `Internal`, `Private`,
  `Protected`, `ProtectedInternal`, `PrivateProtected`, `File`.

(Extensions live under `src/Synto/Decorations/`; marker attributes under `src/Synto/Templating/Decorations/`.)

# Invariants

- These compose **on top of** [templating](/subsystems/templating.md) output — they
  rewrite an already-quoted declaration, they do not participate in binding-time staging.

# Related

- [Templating](/subsystems/templating.md) · [Synto runtime](/projects/synto-runtime.md)
