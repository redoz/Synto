---
type: Subsystem
title: Decorations
description: Post-quote declaration markers that modify a quoted declaration's identifier, visibility, sealed-ness, and base types.
resource: src/Synto.Core/Templating
project: projects/synto-runtime
entrypoints: ["src/Synto.Core/Templating/Decorations/Surface/IdentifierAttribute.cs:6"]
tags: [decorations, declarations, templating]
timestamp: 2026-07-05T00:00:00Z
---

# Responsibility

A small, newest-built surface: markers applied to a quoted declaration that
**decorate** it after quoting — rename it, set its access, seal it, or add base
types. Each attribute pairs with an `Apply…AttributeExtensions` method that
performs the post-quote rewrite.

# Key files

| Marker | Attribute | Extension | Purpose |
|--------|-----------|-----------|---------|
| Identifier | `Templating/Decorations/Surface/IdentifierAttribute.cs:6` | `Decorations/EmitHelpers/IdentifierAttributeExtensions.cs:8` | Rename declaration (+ its constructors). |
| Visibility | `Templating/Decorations/Surface/VisibilityAttribute.cs:6` | `Decorations/EmitHelpers/VisibilityAttributeExtensions.cs:11` | Set access modifiers (via `Access`). |
| Sealed | `Templating/Decorations/Surface/SealedAttribute.cs:6` | `Decorations/EmitHelpers/SealedAttributeExtensions.cs:9` | Add the `sealed` keyword. |
| Implements | `Templating/Decorations/Surface/ImplementsAttribute.cs:6` | `Decorations/EmitHelpers/ImplementsAttributeExtensions.cs:8` | Add an interface base type (repeatable). |
| Inherits | `Templating/Decorations/Surface/InheritsAttribute.cs:6` | `Decorations/EmitHelpers/InheritsAttributeExtensions.cs:8` | Add a base class (prepended). |

- `Templating/Decorations/Surface/Access.cs:3` — the `Access` enum: `Public`, `Internal`, `Private`,
  `Protected`, `ProtectedInternal`, `PrivateProtected`, `File`.

(Paths are relative to `src/Synto.Core/`: marker attributes under
`src/Synto.Core/Templating/Decorations/Surface/`, the post-quote extensions under
`src/Synto.Core/Decorations/EmitHelpers/`. The generator-side decoration pass lives with
its feature at `src/Synto.Generators/Templating/Decorations/DecorateStep.cs`.)

# Invariants

- These compose **on top of** [templating](/subsystems/templating.md) output — they
  rewrite an already-quoted declaration, they do not participate in binding-time staging.

# Related

- [Templating](/subsystems/templating.md) · [Synto.Core runtime](/projects/synto-runtime.md)
