using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Templating.Emit;

namespace Synto.Generators.Templating.Quoting;

/// <summary>
/// Discovers type/namespace-bound simple names in a <c>[Template]</c> body and records, per name node, its
/// <c>global::</c>-rooted fully-qualified substitute — so the quoter emits self-contained output (spec
/// 2026-07-10-quote-time-type-qualification). Only names that BIND to an <see cref="INamedTypeSymbol"/> as a
/// type/namespace reference are recorded; type parameters, <c>var</c>, non-type symbols (locals, members, builder
/// facades), already-<c>global::</c>-rooted names, and the Synto surface are skipped. A written-out qualified name
/// is keyed on its OUTERMOST node (its dotted segments are not descended into — one substitute). A GENERIC name,
/// however, IS descended into after being recorded, so each concrete short-named type argument also gets its own
/// substitute (QualifyStep re-quotes the authored type-arg subtree, so an undescended arg would emit bare). Like
/// <see cref="Builders.FacadeCallFinder"/> it is a <see cref="TemplateScopedWalker"/>: a nested
/// child <c>[Template]</c> subtree is skipped wholesale (the child qualifies itself). Runs inside the transform;
/// captures no symbol/model — only the substitute <see cref="NameSyntax"/> (a plain syntax value).
/// </summary>
internal sealed class QualificationFinder : TemplateScopedWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<SyntaxNode, NameSyntax> _qualifications = new();

    private QualificationFinder(SemanticModel semanticModel, TemplateScope scope)
        : base(scope) => _semanticModel = semanticModel;

    public static IReadOnlyDictionary<SyntaxNode, NameSyntax> FindQualifications(
        SemanticModel semanticModel, SyntaxNode body, TemplateScope scope)
    {
        var finder = new QualificationFinder(semanticModel, scope);
        finder.Visit(body);
        return finder._qualifications;
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.IsVar) return;             // never expand 'var' to its inferred type
        TryQualify(node);                   // identifier has no child names to descend
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        TryQualify(node);                   // qualify the generic name itself (if type-bound)…
        base.VisitGenericName(node);        // …AND descend into its type arguments: a concrete short-named arg
                                            // (e.g. StringBuilder in List<StringBuilder>) needs its OWN substitute,
                                            // since QualifyStep re-quotes the AUTHORED type-arg subtree via `next`
                                            // (not the symbol-baked one), so an undescended arg would emit bare.
                                            // A type parameter / [Splice] arg binds to no INamedTypeSymbol ⇒ skipped
                                            // here and left to its own ReplaceStep (fired through the same `next`).
    }

    public override void VisitQualifiedName(QualifiedNameSyntax node)
    {
        if (TryQualify(node)) return;       // outermost qualified name wins; do not descend into segments
        base.VisitQualifiedName(node);
    }

    private bool TryQualify(SyntaxNode node)
    {
        // Already global::-rooted (parent alias-qualified) — leave as authored.
        if (node.Parent is AliasQualifiedNameSyntax)
            return false;

        // A name occupying a SimpleName-ONLY slot — the member of a member access (`System.Console`, `x.Member`), a
        // member-binding (`?.Member`), or the right segment of a qualified name — must NOT be rewritten to a
        // NameSyntax that may be a QualifiedName: that would break the parent's SimpleNameSyntax slot (CS1503). It is
        // redundant anyway — the receiver/left already supplies the namespace context, and the reference's ROOT
        // (the member-access expression / qualified-name left) is qualified in its own right when it is a bare name.
        if (node.Parent is MemberAccessExpressionSyntax mae && mae.Name == node)
            return false;
        if (node.Parent is MemberBindingExpressionSyntax mbe && mbe.Name == node)
            return false;
        if (node.Parent is QualifiedNameSyntax qn && qn.Right == node)
            return false;

        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is not INamedTypeSymbol type)
            return false;                   // type parameters, methods, locals, members, unbound facades

        // Exclude the Synto surface (mirrors TransplantCarrierUsings' Synto/Synto.* exclusion).
        string ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns == "Synto" || ns.StartsWith("Synto.", StringComparison.Ordinal))
            return false;

        _qualifications[node] = QualifiedNameFactory.ForSymbol(type);
        return true;
    }
}
