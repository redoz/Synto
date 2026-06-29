using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Assembles the generated document for one template: builds the factory method (via the supplied
/// <paramref name="buildFactory"/> seam), wraps it in the target type's ancestry, injects the file-local
/// helpers the factory actually references, merges usings, prepends the <c>#nullable enable</c> directive,
/// formats, and returns the emitted (file-name, source) pair. Runs entirely inside the generator transform;
/// nothing is captured into pipeline state (only the resulting source text flows out).
/// </summary>
internal static class TemplateDocumentBuilder
{
    public static (string FileName, string Source)? Build(
        List<DiagnosticInfo> diagnostics,
        TemplateInfo template,
        Func<UsingDirectiveSet, MethodDeclarationSyntax?> buildFactory)
    {
        // we use this to collect additional usings that are required throughout the source-generation process
        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        // this is null if the template processing failed, but then diagnostics should have been added, so we just exit
        if (buildFactory(additionalUsings) is not { } syntaxFactoryMethod)
            return null;

        var targetClassDecl = (ClassDeclarationSyntax)template.Target.Type!.DeclaringSyntaxReferences[0].GetSyntax();

        MemberDeclarationSyntax targetSyntax = ClassDeclaration(targetClassDecl.Identifier)
            .WithModifiers(targetClassDecl.Modifiers)
            .AddMembers(syntaxFactoryMethod);

        targetSyntax = targetSyntax.WithAncestryFrom(template.Target.Type);

        // The factory body calls the emitted helpers (value.ToSyntax() / typeof(T).ToTypeSyntax() /
        // expr.OrNullLiteralExpression()) as extension methods. Rather than relying on an injected
        // internal copy in `namespace Synto` (which would collide with Synto.Core's public copies under
        // CS0121), emit each used helper as a `file static class` into THIS compilation unit, in the SAME
        // namespace scope as the factory. A `file` type is invisible to other files, so it can never
        // collide, and an extension method in the enclosing/global namespace resolves with no `using` at
        // all — which is why generated files no longer carry `using Synto;`.
        //
        // WHICH helpers to emit is decided by SCANNING the just-built factory syntax for real calls to a
        // known helper method, rather than by flags the factory-builder happens to set. This is robust by
        // construction: any helper a template's output references is detected and injected, so the
        // injected surface is complete for whatever the generator emits.
        var helpers = FindReferencedHelpers(syntaxFactoryMethod);

        // Place the helper classes in the same scope as the factory: inside its (file-scoped or block)
        // namespace if it has one, otherwise directly in the global compilation unit.
        if (helpers.Count > 0)
        {
            var helperDeclarations = helpers.Select(h => (MemberDeclarationSyntax)h.Declaration).ToArray();

            targetSyntax = targetSyntax switch
            {
                FileScopedNamespaceDeclarationSyntax fileNs => fileNs.AddMembers(helperDeclarations),
                NamespaceDeclarationSyntax ns => ns.AddMembers(helperDeclarations),
                _ => targetSyntax, // global namespace: helpers are added to the compilation unit below
            };
        }

        var compilationUnit = CompilationUnit()
            .AddMembers(targetSyntax);

        // In the global-namespace case the helpers weren't folded into a namespace member above, so add
        // them as top-level members of the compilation unit alongside the factory.
        if (helpers.Count > 0 && targetSyntax is not (FileScopedNamespaceDeclarationSyntax or NamespaceDeclarationSyntax))
            compilationUnit = compilationUnit.AddMembers(helpers.Select(h => (MemberDeclarationSyntax)h.Declaration).ToArray());

        // The compilation unit's usings: the quoter's required usings + any collected during processing +
        // the usings the emitted helpers themselves need (e.g. `using SF = ...SyntaxFactory;`). C# requires
        // usings before any declaration, so the helper usings are merged in here (deduped).
        var usings = CSharpSyntaxQuoter.RequiredUsings()
            .Union(additionalUsings)
            .ToList();

        foreach (var helper in helpers)
            MergeUsings(usings, helper.Usings);

        compilationUnit = compilationUnit.AddUsings(usings.ToArray());

        // Emit inside an explicit `#nullable enable` context. The generated factory carries nullable
        // annotations (`ExpressionSyntax?` parameters, `node.X!` suppressions), so without this directive
        // a consumer compiling the auto-generated file gets CS8669 ("annotation should only be used in a
        // '#nullable' context"). Prepending the directive matches how SurfaceInjectionGenerator and
        // DiagnosticsGenerator emit their output.
        compilationUnit = compilationUnit.WithLeadingTrivia(
            Trivia(
                NullableDirectiveTrivia(
                    Token(SyntaxKind.EnableKeyword),
                    isActive: true)));

        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8).ToString();

        return ($"{template.Target.FullName}.{template.Source!.Identifier}.g.cs", sourceText);
    }

    private static void MergeUsings(List<UsingDirectiveSyntax> target, IEnumerable<UsingDirectiveSyntax> additions)
    {
        foreach (var addition in additions)
        {
            if (!target.Any(existing => existing.IsEquivalentTo(addition, topLevel: false)))
                target.Add(addition);
        }
    }

    /// <summary>
    /// Scans a generated factory method for real calls to a known runtime helper and returns the
    /// <c>file static class</c> for each referenced helper (deduplicated, at most one per helper).
    /// </summary>
    /// <remarks>
    /// Detection matches <see cref="MemberAccessExpressionSyntax"/> whose <c>.Name</c> identifier text is
    /// a helper method name — i.e. an actual emitted call such as <c>value.ToSyntax()</c> or
    /// <c>typeof(T).ToTypeSyntax()</c>. Crucially this does NOT match QUOTED template content: when a
    /// template's own body references such a name, the quoter emits it as a string literal argument
    /// (<c>IdentifierName("ToSyntax")</c>), not as a member-access identifier, so quoted content never
    /// triggers a spurious injection (and a template literally calling <c>.ToSyntax()</c> in its body is
    /// not double-injected).
    /// </remarks>
    private static List<FileLocalHelpers.Helper> FindReferencedHelpers(MethodDeclarationSyntax factoryMethod)
    {
        var referencedMethodNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var memberAccess in factoryMethod.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            referencedMethodNames.Add(memberAccess.Name.Identifier.ValueText);

        var helpers = new List<FileLocalHelpers.Helper>();
        foreach (var entry in FileLocalHelpers.Entries)
        {
            if (referencedMethodNames.Contains(entry.MethodName))
                helpers.Add(entry.Helper);
        }

        return helpers;
    }
}
