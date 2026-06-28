---
type: Subsystem
title: Decorations
description: Post-quote declaration markers that modify a quoted declaration's identifier, visibility, sealed-ness, and base types.
resource: src/Synto/Templating
project: projects/synto-runtime
entrypoints: ["src/Synto/Templating/IdentifierAttribute.cs:6"]
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
| Identifier | `Templating/IdentifierAttribute.cs:6` | `IdentifierAttributeExtensions.cs:8` | Rename declaration (+ its constructors). |
| Visibility | `Templating/VisibilityAttribute.cs:6` | `VisibilityAttributeExtensions.cs:11` | Set access modifiers (via `Access`). |
| Sealed | `Templating/SealedAttribute.cs:6` | `SealedAttributeExtensions.cs:9` | Add the `sealed` keyword. |
| Implements | `Templating/ImplementsAttribute.cs:6` | `ImplementsAttributeExtensions.cs:8` | Add an interface base type (repeatable). |
| Inherits | `Templating/InheritsAttribute.cs:6` | `InheritsAttributeExtensions.cs:8` | Add a base class (prepended). |

- `Templating/Access.cs:3` — the `Access` enum: `Public`, `Internal`, `Private`,
  `Protected`, `ProtectedInternal`, `PrivateProtected`, `File`.

(Extensions live at the `src/Synto/` root; attributes under `src/Synto/Templating/`.)

# Invariants

- These compose **on top of** [templating](/subsystems/templating.md) output — they
  rewrite an already-quoted declaration, they do not participate in binding-time staging.

# Related

- [Templating](/subsystems/templating.md) · [Synto runtime](/projects/synto-runtime.md)
