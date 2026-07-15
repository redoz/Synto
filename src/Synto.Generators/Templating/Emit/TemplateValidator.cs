using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating.Emit;

/// <summary>
/// Up-front structural validation of a template target: the target type must be declared in source, be a
/// <c>class</c>, be <c>partial</c>, and have a fully-<c>partial</c> ancestry. Any violation is reported
/// (SY1001-1004) and the build bails before factory construction. The finer-grained mid-build bail gates
/// (invalid splice shapes, impossible cuts, missing converters, …) stay physically in place in the factory
/// builder as small bool guards (locked decision 3 — no control-flow inversion).
/// </summary>
internal static class TemplateValidator
{
    public static bool Validate(List<DiagnosticInfo> diagnostics, string assemblyName, TemplateInfo template)
    {
        if (template.Target.Type.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            diagnostics.Add(Incremental.Diagnostics.TargetNotDeclaredInSource(LocationInfo.CreateFrom(template.Target.GetReferenceLocation()), template.Target.FullName, assemblyName));
            return false;
        }

        if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classSyntax)
        {
            diagnostics.Add(Incremental.Diagnostics.TargetNotClass(LocationInfo.CreateFrom(template.Target.GetReferenceLocation()), template.Target.FullName));
            return false;
        }

        if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Incremental.Diagnostics.TargetNotPartial(LocationInfo.CreateFrom(template.Target.GetReferenceLocation()), template.Target.FullName));
            return false;
        }

        bool EnsureAncestrySupported(ClassDeclarationSyntax classDeclarationSyntax)
        {
            bool ret = true;
            var parent = classDeclarationSyntax.Parent;
            while (parent is TypeDeclarationSyntax parentType)
            {
                if (parentType is not ClassDeclarationSyntax parentClass)
                {
                    // A struct/record/interface ancestor cannot be honored: WithAncestryFrom hard-casts every
                    // ancestor to ClassDeclarationSyntax. Reject it up-front (located at the ancestor's own
                    // identifier) before that cast throws an opaque SY0000 InvalidCastException.
                    diagnostics.Add(Incremental.Diagnostics.TargetAncestorUnsupportedKind(
                        LocationInfo.CreateFrom(parentType.Identifier.GetLocation()),
                        template.Target.FullName,
                        parentType.Identifier.Text,
                        parentType.Keyword.ValueText));
                    return false;
                }

                if (!parentClass.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    diagnostics.Add(Incremental.Diagnostics.TargetAncestorNotPartial(LocationInfo.CreateFrom(template.Target.GetReferenceLocation()), template.Target.FullName, parentClass.Identifier.Text));
                    ret = false;
                }

                parent = parentClass.Parent;
            }

            return ret;
        }

        if (!EnsureAncestrySupported(classSyntax))
            return false;

        return true;
    }
}
