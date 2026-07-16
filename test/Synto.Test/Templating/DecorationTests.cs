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
            .Create(new SurfaceInjectionGenerator(), new TemplateSurfaceInjector())
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

    // Post-quote hook folding (the decoration feature's quoter branch) is covered at the step level by
    // QuoteMiddlewareTest.Decoration_WrapsBaseQuote_InChainedInvocation, exercising DecorateStep dispatched by
    // PerNodePipelineQuoter.

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
        // SY1035 "live template code" is an Info highlight, not an error — narrow to Error so live templates
        // (which now legitimately surface SY1035) still assert no error diagnostics (plan R2).
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

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
        // SY1035 "live template code" is an Info highlight, not an error — narrow to Error so live templates
        // (which now legitimately surface SY1035) still assert no error diagnostics (plan R2).
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

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
                [Template<Factory>]
                [Identifier]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains("ApplyIdentifierAttribute(identifier)", generated, System.StringComparison.Ordinal);
        Assert.Contains("string identifier", generated, System.StringComparison.Ordinal); // injected factory parameter
    }

    [Fact]
    public void Sealed_EmitsApplySealedCall()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template<Factory>]
                [Sealed]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains(".ApplySealedAttribute()", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Visibility_EmitsAccessArg()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template<Factory>]
                [Visibility(Access.File)]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains("ApplyVisibilityAttribute(global::Synto.Templating.Access.File)", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Implements_EmitsInterfaceFqnArg()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template<Factory>]
                [Implements<global::System.IDisposable>]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);
        Assert.Contains("ApplyImplementsAttribute(\"global::System.IDisposable\")", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InheritsThenImplements_EmittedInBaseListOrder()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template<Factory>]
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
                [Template<Factory>]
                [Identifier]
                [Visibility(Access.Internal)]
                [Sealed]
                [Inherits<global::System.Exception>]
                [Implements<global::System.IDisposable>]
                class Shell { public Shell() {} }
            }
            """;
        var generated = RunFactoryAndGetSource(src);

        Assert.Contains("ApplyIdentifierAttribute(identifier)", generated, System.StringComparison.Ordinal);
        Assert.Contains("ApplyVisibilityAttribute(global::Synto.Templating.Access.Internal)", generated, System.StringComparison.Ordinal);
        Assert.Contains(".ApplySealedAttribute()", generated, System.StringComparison.Ordinal);
        Assert.Contains("ApplyInheritsAttribute(\"global::System.Exception\")", generated, System.StringComparison.Ordinal);
        Assert.Contains("ApplyImplementsAttribute(\"global::System.IDisposable\")", generated, System.StringComparison.Ordinal);
        Assert.Contains("string identifier", generated, System.StringComparison.Ordinal);
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
        // A nested child method Impl has bare [Template] + [Visibility(Access.Public)] — it is a foreign child,
        // trimmed from the parent's quoted output AND never visited by the parent's walker. As a bare [Template]
        // it is now a parent-driven sub-template folded into the parent's SINGLE document (an internal static
        // member), so its own [Visibility(Access.Public)] decoration legitimately emits ApplyVisibilityAttribute
        // inside the CHILD sub-template's region of that document.
        // If TemplateScopedWalker correctly skips the child, the parent factory METHOD contains ApplySealedAttribute
        // (from its own [Sealed]) but NOT ApplyVisibilityAttribute (which belongs to the child only).
        var src = """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            [Template<Factory>]
            [Sealed]
            public class Reader
            {
                public string Name { get; } = "";
                [Template]
                [Visibility(Access.Public)]
                public void Impl()
                {
                    var name = Unquote<string>();
                    _ = name;
                }
            }
            """;

        var sources = RunFactoryAndGetSources(src);

        // The parent factory (Reader) is generated. The bare-[Template] child folds into this SAME document as a
        // private/internal sub-template, so scope the leak assertions to the parent factory METHOD region only —
        // the public parent method up to the first `internal static` sub-template that follows it.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        var document = sources["Factory.Reader.g.cs"];
        var parentFactory = ParentFactoryMethodRegion(document);

        // The parent factory carries [Sealed]'s own ApplySealedAttribute (proof the parent is processed).
        Assert.Contains("ApplySealedAttribute", parentFactory, StringComparison.Ordinal);

        // The child's [Visibility(Access.Public)] decoration must NOT appear in the parent factory method.
        // It belongs exclusively to the child sub-template's own region of the shared document.
        Assert.DoesNotContain("ApplyVisibilityAttribute", parentFactory, StringComparison.Ordinal);
    }

    // Returns the text of the public parent factory method (from `public static` up to the first `internal static`
    // sub-template that follows it) so leak assertions target the parent's own body, not a folded-in child.
    private static string ParentFactoryMethodRegion(string document)
    {
        var start = document.IndexOf("public static", StringComparison.Ordinal);
        if (start < 0)
            return document;

        var subTemplateStart = document.IndexOf("internal static", start, StringComparison.Ordinal);
        return subTemplateStart < 0 ? document.Substring(start) : document.Substring(start, subTemplateStart - start);
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
                [Template<Factory>]
                [Foo("hi")]
                class Shell { public Shell() {} }
            }
            """;

        var generated = RunFactoryAndGetSource(src);

        // The user-defined hook must be emitted with the constructor argument as a string literal.
        Assert.Contains("ApplyFooAttribute(\"hi\")", generated, System.StringComparison.Ordinal);
    }

    // ----- Task 20 (#23): executed end-to-end decoration proof --------------------------------------
    // The other decoration facts only assert the generated factory TEXT. This one compiles the generated
    // factory, Assembly.Load()s it, invokes it, and renders the returned declaration — proving at the
    // consumer-visible layer that [Visibility(Public)] + [Sealed] on a `partial class` yield a LEGAL modifier
    // order `public sealed partial class` (the Task 6 / #6 fix; appending `sealed` would be CS0267).
    [Fact]
    public void DecoratedPartial_Executed_RendersPublicSealedPartial()
    {
        var src = """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template<Factory>]
                [Visibility(Access.Public)]
                [Sealed]
                partial class Shell { public Shell() {} }
            }
            """;

        var compilation = _factoryBaseCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(src, cancellationToken: Xunit.TestContext.Current.CancellationToken));
        Assert.Empty(compilation.GetDiagnostics(Xunit.TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        // Only TemplateFactorySourceGenerator: the [Sealed]/[Visibility] markers and the Apply* extension
        // methods resolve against the referenced public Synto.Core surface (running SurfaceInjectionGenerator
        // too would inject internal marker twins and make `[Sealed]` ambiguous).
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var compileErrors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(compileErrors.Count == 0,
            "Generated output did not compile: " + string.Join("; ", compileErrors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

        using var peStream = new System.IO.MemoryStream();
        var emit = output.Emit(peStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, "Emit failed: " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

        var assembly = Assembly.Load(peStream.ToArray());
        var shellFactory = assembly.GetType("Factory")!.GetMethod("Shell")!;
        var built = shellFactory.Invoke(null, null);

        var node = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.SyntaxNode>(built!);
        var rendered = node.NormalizeWhitespace().ToString();
        Assert.Contains("public sealed partial class", rendered, StringComparison.Ordinal);
    }
}
