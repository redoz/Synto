using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators.Templating;

namespace Synto.Test.Templating;

/// <summary>
/// The empirical gate for <see cref="Synto.Templating.TemplateOption.PreserveTrivia"/> (plan Task 13 / #14).
/// Before the fix, <c>CSharpSyntaxQuoterBase.Visit(SyntaxTriviaList)</c> always emitted an empty
/// <c>TriviaList()</c>, so the flag paid the wider trivia-carrying token-emission cost while silently
/// preserving nothing. These facts EXECUTE the generated factory (Emit + Assembly.Load + Invoke) and
/// render the returned node with <c>ToFullString()</c> — the arbiter is the reconstructed text, not a
/// snapshot. Scope is bounded to whitespace + comments; structured/directive trivia stays deferred.
/// </summary>
public class PreserveTriviaTest
{
    private static readonly MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private static Compilation CompilationWithSource(string source) =>
        CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    // Generate + compile + execute the factory for a template whose body carries a single-line comment,
    // a block comment, and meaningful indentation, and return the reconstructed node's ToFullString().
    private static string RenderExecutedFactory(string optionsArg)
    {
        // The `// marker` (trailing trivia of the first `;`) and `/* block */` (trailing trivia of the
        // second `;`) exercise single-line and multi-line comment quoting; the newline between the two
        // statements exercises end-of-line quoting. (Leading trivia of a block's FIRST statement is
        // stripped by a separate, pre-existing first-token normalization upstream of the quoter — out of
        // scope for this task, which targets only Visit(SyntaxTriviaList); see the Deferred note.)
        var source =
            "using System;\n" +
            "using Synto.Templating;\n" +
            "\n" +
            "partial class Factory {}\n" +
            "\n" +
            "public class TestClass {\n" +
            "    [Template<Factory>" + optionsArg + "]\n" +
            "    static void Build() {\n" +
            "        Console.WriteLine(\"one\"); // marker comment\n" +
            "        Console.WriteLine(\"two\"); /* block comment */\n" +
            "    }\n" +
            "}\n";

        var compilation = CompilationWithSource(source);

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var compileErrors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(compileErrors.Count == 0,
            "Generated output did not compile: " + string.Join("; ", compileErrors.Select(d => d.Id + " " + d.GetMessage())));

        using var peStream = new System.IO.MemoryStream();
        var emit = output.Emit(peStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success,
            "Emit failed: " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage())));

        var assembly = Assembly.Load(peStream.ToArray());
        var built = assembly.GetType("Factory")!.GetMethod("Build")!.Invoke(null, null);
        var node = Assert.IsAssignableFrom<SyntaxNode>(built!);
        return node.ToFullString();
    }

    [Fact]
    public void PreserveTrivia_RoundTrip_KeepsCommentsAndWhitespace()
    {
        var rendered = RenderExecutedFactory("(Options = TemplateOption.PreserveTrivia)");

        // The comment texts survive the quote/re-emit round-trip (leading trivia of `Console`, trailing
        // trivia of `;`), and the whitespace/newline shape is retained (not collapsed onto one line).
        Assert.Contains("// marker comment", rendered, StringComparison.Ordinal);
        Assert.Contains("/* block comment */", rendered, StringComparison.Ordinal);
        Assert.Contains("\n", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutFlag_StripsTrivia()
    {
        var rendered = RenderExecutedFactory("");

        // Default path (includeTrivia:false) reconstructs bare tokens: no comment text survives.
        Assert.DoesNotContain("// marker comment", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("/* block comment */", rendered, StringComparison.Ordinal);
    }
}
