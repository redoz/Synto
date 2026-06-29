using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Test.Templating;

/// <summary>
/// Verifies that the six decoration marker types (post-quote declaration decorations feature) are
/// injected as <c>internal</c> into the consumer compilation by <see cref="SurfaceInjectionGenerator"/>.
/// </summary>
public class DecorationTests
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
    private static readonly MetadataReference RoslynCSharpReference = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax).Assembly.Location);
    private static readonly MetadataReference RoslynCommonReference = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location);
    private static readonly MetadataReference SystemLinqReference = MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
    private static readonly MetadataReference SystemCollectionsReference = MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
    private static readonly MetadataReference ImmutableCollectionsReference = MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location);

    [Fact]
    public void MarkerSurface_IsInjectedAsInternal()
    {
        var compilation = CSharpCompilation.Create("DecorationSurfaceTest",
            syntaxTrees: [],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new SurfaceInjectionGenerator())
            .RunGenerators(compilation, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        var allGeneratedSource = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));

        Assert.Contains("internal enum Access", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class IdentifierAttribute", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class VisibilityAttribute", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class SealedAttribute", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ImplementsAttribute<TInterface>", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class InheritsAttribute<TBase>", allGeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Quoter_FoldsPostQuoteHook_OntoNodeQuote()
    {
        var tree = CSharpSyntaxTree.ParseText("class Foo {}");
        var compilation = CSharpCompilation.Create("c",
            new[] { tree },
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First();

        var hooks = new Dictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>
        {
            [node] = ImmutableArray.Create(new AppliedDecoration(
                "ApplySealedAttribute", ImmutableArray<ExpressionSyntax>.Empty)),
        };
        var quoter = new TemplateSyntaxQuoter(model,
            new Dictionary<SyntaxNode, ExpressionSyntax>(), new HashSet<SyntaxNode>(),
            includeTrivia: false, postQuoteHooks: hooks);

        var expr = quoter.Visit(node);
        Assert.Contains("ApplySealedAttribute()", expr!.ToString());
    }

    // ----- Task 4: decoration round-trip through the factory generator -------------------------------
    // A carrier marked with each decoration marker runs through TemplateFactorySourceGenerator; the
    // generated FACTORY source must carry the corresponding Apply… hook chain (and, for [Identifier], the
    // injected `string` parameter). Asserts the generated factory text — not consumer output.

    private static CSharpCompilation CreateFactoryCompilation() =>
        CSharpCompilation.Create("Test",
            Array.Empty<SyntaxTree>(),
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
                RoslynCSharpReference,
                RoslynCommonReference,
                SystemLinqReference,
                SystemCollectionsReference,
                ImmutableCollectionsReference,
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _factoryBaseCompilation = CreateFactoryCompilation();
    private readonly GeneratorDriver _factoryDriver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    /// <summary>Runs the factory generator over <paramref name="source"/> and returns the single generated factory source.</summary>
    private string RunFactoryAndGetSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = _factoryBaseCompilation.AddSyntaxTrees(syntaxTree);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var result = _factoryDriver.RunGenerators(compilation);
        var runResult = result.GetRunResult();
        Assert.Empty(runResult.Diagnostics);

        return runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Single()
            .SourceText.ToString();
    }

    /// <summary>
    /// Runs the factory generator over <paramref name="source"/> and returns all generated factory sources
    /// keyed by hint name. Use when the carrier contains a nested child <c>[Template]</c> that produces
    /// its own separate factory file alongside the parent's.
    /// </summary>
    private Dictionary<string, string> RunFactoryAndGetSources(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = _factoryBaseCompilation.AddSyntaxTrees(syntaxTree);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var result = _factoryDriver.RunGenerators(compilation);
        var runResult = result.GetRunResult();
        Assert.Empty(runResult.Diagnostics);

        return runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);
    }

    [Fact]
    public void Identifier_InjectsStringParam_AndEmitsApplyCall()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Identifier]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains("ApplyIdentifierAttribute(identifier)", generated);
        Assert.Contains("string identifier", generated); // injected factory parameter
    }

    [Fact]
    public void Sealed_EmitsApplySealedCall()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Sealed]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains(".ApplySealedAttribute()", generated);
    }

    [Fact]
    public void Visibility_EmitsAccessArg()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Visibility(Access.File)]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains("ApplyVisibilityAttribute(global::Synto.Templating.Access.File)", generated);
    }

    [Fact]
    public void Implements_EmitsInterfaceFqnArg()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Implements<global::System.IDisposable>]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains("ApplyImplementsAttribute(\"global::System.IDisposable\")", generated);
    }

    [Fact]
    public void InheritsThenImplements_EmittedInBaseListOrder()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Inherits<global::System.Exception>]
                [Implements<global::System.IDisposable>]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);

        var inheritsIndex = generated.IndexOf("ApplyInheritsAttribute", StringComparison.Ordinal);
        var implementsIndex = generated.IndexOf("ApplyImplementsAttribute", StringComparison.Ordinal);

        Assert.True(inheritsIndex >= 0, "ApplyInheritsAttribute call missing");
        Assert.True(implementsIndex >= 0, "ApplyImplementsAttribute call missing");
        Assert.True(inheritsIndex < implementsIndex, "ApplyInheritsAttribute must precede ApplyImplementsAttribute in the chain");
    }

    [Fact]
    public void AllMarkersComposed_ChainAllApplyCalls()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Identifier]
                [Visibility(Access.Internal)]
                [Sealed]
                [Inherits<global::System.Exception>]
                [Implements<global::System.IDisposable>]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);

        Assert.Contains("ApplyIdentifierAttribute(identifier)", generated);
        Assert.Contains("ApplyVisibilityAttribute(global::Synto.Templating.Access.Internal)", generated);
        Assert.Contains(".ApplySealedAttribute()", generated);
        Assert.Contains("ApplyInheritsAttribute(\"global::System.Exception\")", generated);
        Assert.Contains("ApplyImplementsAttribute(\"global::System.IDisposable\")", generated);
        Assert.Contains("string identifier", generated);
    }

    // ----- Task 6: scope isolation + user-defined extensibility proof ----------------------------------

    /// <summary>
    /// Proves scope isolation: a <c>[Visibility]</c> decoration on a nested child <c>[Template]</c> method
    /// is owned by the CHILD's factory and must NOT appear in the PARENT factory.
    /// The parent carrier has <c>[Sealed]</c> but no <c>[Visibility]</c> of its own, so any
    /// <c>ApplyVisibilityAttribute</c> call in the parent factory would be a TemplateScopedWalker leak.
    /// </summary>
    [Fact]
    public void DecorationOnNestedChild_DoesNotLeakToParentFactory()
    {
        // Carrier Reader has [Sealed] and a field (so the parent's quoted output is non-empty).
        // A nested child method Impl has [Template] + [Visibility(Access.Public)] — it is a foreign child,
        // trimmed from the parent's quoted output AND never visited by the parent's walker.
        // If TemplateScopedWalker correctly skips the child, the parent factory contains ApplySealedAttribute
        // (from its own [Sealed]) but NOT ApplyVisibilityAttribute (which belongs to the child only).
        var src = """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            [Template(typeof(Factory))]
            [Sealed]
            public class Reader
            {
                public string Name { get; } = "";
                [Template(typeof(Factory))]
                [Visibility(Access.Public)]
                public void Impl()
                {
                    var name = Parameter<string>();
                    _ = name;
                }
            }
            """;

        var sources = RunFactoryAndGetSources(src);

        // The parent factory (Reader) is generated.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        var parentFactory = sources["Factory.Reader.g.cs"];

        // The parent factory carries [Sealed]'s own ApplySealedAttribute (proof the parent is processed).
        Assert.Contains("ApplySealedAttribute", parentFactory, StringComparison.Ordinal);

        // The child's [Visibility(Access.Public)] decoration must NOT appear in the parent factory.
        // It belongs exclusively to the child's own factory (Factory.TypedGetter.g.cs).
        Assert.DoesNotContain("ApplyVisibilityAttribute", parentFactory, StringComparison.Ordinal);
    }

    /// <summary>
    /// Proves open-by-construction extensibility: a user-defined <c>[Foo("hi")]</c> attribute whose
    /// <c>ApplyFooAttribute&lt;T&gt;(this T, string)</c> extension method exists in the same assembly
    /// flows through the same decoration pipeline and emits <c>ApplyFooAttribute("hi")</c> in the
    /// generated factory — no built-in knowledge of <c>[Foo]</c> required.
    /// </summary>
    [Fact]
    public void UserDefinedDecoration_FlowsThroughSamePath()
    {
        // ApplyFooAttribute is constrained on TypeDeclarationSyntax (fully-qualified) so the
        // convention-resolution path validates the this-type against the decorated class node.
        var src = """
            using Synto.Templating;
            public sealed class FooAttribute : System.Attribute { public FooAttribute(string tag) {} }
            public static class FooExt {
                public static T ApplyFooAttribute<T>(this T node, string tag)
                    where T : Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax => node;
            }
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Foo("hi")]
                class Shell { public Shell() {} }
            }
            """;

        var generated = RunFactoryAndGetSource(src);

        // The user-defined hook must be emitted with the constructor argument as a string literal.
        Assert.Contains("ApplyFooAttribute(\"hi\")", generated);
    }
}
