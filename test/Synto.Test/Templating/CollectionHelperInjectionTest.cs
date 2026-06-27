using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Guards the file-local collection helper (plan Task 5 / spec §5.3 / §6): the <c>BuildList</c> helper is
/// REGISTERED for scan-based injection, rewritten to a <c>file</c>-scoped type (so it can never collide with
/// <c>Synto.Core</c>'s public copy), and — compiled against ONLY the injected surface (no <c>Synto.Core</c>
/// reference) together with a hand-written caller — produces zero error diagnostics. A dropped/uninjected or
/// mis-shaped helper surfaces here as CS0103/CS1061/CS0246.
/// </summary>
public class CollectionHelperInjectionTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    [Fact]
    public void BuildListHelper_IsRegistered_AndFileScoped()
    {
        var entry = FileLocalHelpers.Entries.Single(e => e.MethodName == "BuildList");

        // The single source of truth for the scan must rewrite the embedded public helper to a `file` type;
        // otherwise the injected copy would collide with Synto.Core's public copy when both are referenced.
        Assert.Contains(entry.Helper.Declaration.Modifiers, m => m.IsKind(SyntaxKind.FileKeyword));
        Assert.DoesNotContain(entry.Helper.Declaration.Modifiers, m => m.IsKind(SyntaxKind.PublicKeyword));
    }

    [Fact]
    public void CollectionHelper_IsInjected_WhenReferenced()
    {
        var entry = FileLocalHelpers.Entries.Single(e => e.MethodName == "BuildList");

        // Reconstruct exactly what the generator emits into a factory file: the helper's required usings, the
        // rewritten `file static class`, plus a hand-written caller (standing in for a generated factory body)
        // that mixes a node RUN and a FIXED node through BuildList — the incorporation logic Tasks 6–7 target.
        var usings = string.Concat(entry.Helper.Usings.Select(u => u.ToFullString()));
        var declaration = entry.Helper.Declaration.NormalizeWhitespace().ToFullString();

        const string caller =
            """

            file static class Caller
            {
                static Microsoft.CodeAnalysis.SyntaxList<Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax> Build(
                    System.Collections.Generic.IEnumerable<Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax> run,
                    Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax fixedNode)
                {
                    return CollectionSyntaxExtensions.BuildList<Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax>(
                        CollectionSyntaxExtensions.ListSegment<Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax>.Run(run),
                        fixedNode);
                }
            }
            """;

        var source = usings + "\n" + declaration + "\n" + caller;

        var compilation = CSharpCompilation.Create("CollectionHelperInjectionTest",
            [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                // DELIBERATELY NO Synto.Core: the only Synto type available is the injected file-local helper.
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "The injected collection helper did not compile against only the injected surface: "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }
}
