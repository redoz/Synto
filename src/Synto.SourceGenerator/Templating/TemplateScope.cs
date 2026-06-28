using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// The single source of truth for "what is foreign to this parent <c>[Template]</c> carrier" (Capability 1).
/// A method-level <c>[Template]</c> nested inside a class-level <c>[Template]</c> carrier is a SIBLING child
/// template: it is independently picked up by <c>ForAttributeWithMetadataName</c> and generates its OWN factory,
/// so when the PARENT carrier is processed the child's whole subtree must be excluded — both trimmed from the
/// quoted output and NEVER VISITED by the parent's template finders (otherwise the child's own staged roots /
/// parameters / type-parameters leak into the parent factory's signature).
/// <para>
/// The exclusion is enforced structurally: <see cref="TemplateScopedWalker"/> consults <see cref="IsForeign"/>
/// and skips a foreign method subtree wholesale, so no per-finder consumption site has to remember to filter.
/// </para>
/// Pure data computed inside the GenerateTemplate transform — no <c>ISymbol</c>/<c>SemanticModel</c>/
/// <c>SyntaxNode</c> is captured into cached pipeline state. An empty scope (a non-class carrier, or a class with
/// no nested child templates) behaves exactly like the unscoped walk.
/// </summary>
internal sealed class TemplateScope
{
    private static readonly TemplateScope EmptyScope = new(Array.Empty<MethodDeclarationSyntax>(), new HashSet<MethodDeclarationSyntax>());

    private readonly HashSet<MethodDeclarationSyntax> _foreign;

    private TemplateScope(IReadOnlyList<MethodDeclarationSyntax> foreignChildren, HashSet<MethodDeclarationSyntax> foreign)
    {
        ForeignChildren = foreignChildren;
        _foreign = foreign;
    }

    /// <summary>The foreign child-template method roots (used by the transform to trim them from the quoted output).</summary>
    public IReadOnlyList<MethodDeclarationSyntax> ForeignChildren { get; }

    /// <summary>O(1) membership: is <paramref name="node"/> a foreign child-template method root of this carrier?</summary>
    public bool IsForeign(MethodDeclarationSyntax node) => _foreign.Contains(node);

    /// <summary>
    /// Build the scope for a carrier. Only a class-level carrier can host foreign child templates; a method-level
    /// carrier's own <c>Source.Syntax</c> IS the method (never a nested sibling), so it gets an empty scope, as
    /// does a class with no nested child templates.
    /// </summary>
    public static TemplateScope ForCarrier(SemanticModel semanticModel, SyntaxNode carrier)
    {
        if (carrier is not ClassDeclarationSyntax)
            return EmptyScope;

        var children = ChildTemplateFinder.FindChildTemplates(semanticModel, carrier);
        if (children.Count == 0)
            return EmptyScope;

        return new TemplateScope(children, new HashSet<MethodDeclarationSyntax>(children));
    }
}
