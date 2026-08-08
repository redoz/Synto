using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;


namespace Synto.Test.Templating;

/// <summary>
/// Declaration-rules facts for the two-form <c>[Template]</c> surface (spec §4). A top-level parameterless
/// <c>[Template]</c> carries no target and is misuse — it reports <c>SY1033</c>. A parameterless <c>[Template]</c>
/// nested inside a <c>[Template&lt;T&gt;]</c> carrier is a parent-driven <c>internal</c> sub-template, folded into
/// the parent's single document. A generic <c>[Template&lt;T&gt;]</c> nested inside a carrier illegally pins its own
/// target — it reports <c>SY1034</c> and produces no sibling document.
/// </summary>
public class TemplateAttributeFormTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static readonly MetadataReference[] References =
    [
        CorlibReference,
        NetStandardReference,
        SystemRuntimeReference,
        MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private static GeneratorDriverRunResult Run(string source)
    {
        var compilation = CSharpCompilation.Create("TemplateAttributeFormTest",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        return driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();
    }

    [Fact]
    public void TopLevelBareTemplate_ReportsSY1033_NoSource()
    {
        var runResult = Run(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass
            {
                [Template]
                static void Sample()
                {
                    Console.WriteLine("Hello world");
                }
            }
            """);

        var diag = Assert.Single(runResult.Diagnostics, d => d.Id == "SY1033");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);

        Assert.Empty(runResult.Results.SelectMany(r => r.GeneratedSources));
    }

    [Fact]
    public void NestedBareTemplate_InGenericCarrier_BuildsPrivateSubTemplate()
    {
        // Phase 2: a bare [Template] nested inside a [Template<Factory>] carrier is a PARENT-DRIVEN internal
        // sub-template — exempt from SY1033 (nested), never SY1034 (it pins no target of its own), and built by
        // the parent into the parent's single document as an `internal static` method (no independent sibling).
        var runResult = Run(
            """
            using System;
            using System.Collections.Generic;
            using Synto.Templating;
            using static Synto.Templating.Template;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

            partial class Factory {}

            [Template<Factory>]
            public class Reader
            {
                // A real member so the PARENT carrier has a quotable body of its own (an empty carrier is a
                // separate degenerate case); the nested bare [Template] rides in as a internal sub-template.
                public void Hello() { System.Console.WriteLine("hi"); }

                [Template]
                public ExpressionSyntax Child()
                {
                    var name = Unquote<string>();
                    return IdentifierName(name);
                }
            }
            """);

        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "SY1033");
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "SY1034");
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        // Exactly one document (the parent's) — no separate Factory.Child.g.cs sibling — and the child is folded
        // into it as an `internal static` sub-template.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        Assert.DoesNotContain("Factory.Child.g.cs", sources.Keys);
        Assert.Contains("internal static", sources["Factory.Reader.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void NestedGenericTemplate_InGenericCarrier_ReportsSY1034_NoSibling_ParentStillBuilds()
    {
        // Phase 2 (SY-B): a generic [Template<Factory>] nested inside a [Template<Factory>] carrier illegally pins a
        // target of its own. It must report exactly one SY1034 and produce NO sibling document — a nested template
        // inherits its parent carrier's target and rides in as a bare [Template] sub-template instead. The parent
        // carrier is unaffected: the offending child stays a trimmed foreign child (never a built sub-template), so
        // the parent's single document still emits.
        var runResult = Run(
            """
            using System;
            using System.Collections.Generic;
            using Synto.Templating;
            using static Synto.Templating.Template;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

            partial class Factory {}

            [Template<Factory>]
            public class Reader
            {
                public void Hello() { System.Console.WriteLine("hi"); }

                [Template<Factory>]
                public ExpressionSyntax Child()
                {
                    var name = Unquote<string>();
                    return IdentifierName(name);
                }
            }
            """);

        var diag = Assert.Single(runResult.Diagnostics, d => d.Id == "SY1034");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);

        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        // Parent factory still emits; the nested generic child produces NO independent sibling document.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        Assert.DoesNotContain("Factory.Child.g.cs", sources.Keys);
    }
}
