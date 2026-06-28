using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Base walker for every per-template finder that must respect the carrier's ownership boundary (Capability 1).
/// A foreign nested child <c>[Template]</c> method subtree is skipped wholesale — it is NEVER VISITED (skip-during)
/// rather than filtered at each finder's consumption site, which is what allowed a child's staged roots / params /
/// type-params to leak through any finder that forgot the per-site guard.
/// <para>
/// Derived finders keep ALL their existing overrides (<c>DefaultVisit</c>, <c>VisitParameter</c>,
/// <c>VisitTypeParameter</c>, <c>VisitInvocationExpression</c>, …); dispatch still routes through them for every
/// non-foreign node. They must NOT override <see cref="VisitMethodDeclaration"/> themselves.
/// </para>
/// An empty <see cref="TemplateScope"/> makes this behave exactly like a plain <see cref="CSharpSyntaxWalker"/>.
/// </summary>
internal abstract class TemplateScopedWalker : CSharpSyntaxWalker
{
    private readonly TemplateScope _scope;

    protected TemplateScopedWalker(TemplateScope scope)
    {
        _scope = scope;
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // Skip the WHOLE subtree of a foreign child template — its roots belong to the child's own factory.
        if (_scope.IsForeign(node))
            return;

        base.VisitMethodDeclaration(node);
    }
}
