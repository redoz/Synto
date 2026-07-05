using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Task 1 facts for the generic <c>[Template&lt;T&gt;]</c> discovery path. The generic attribute lands ALONGSIDE
/// the existing <c>[Template(typeof(T))]</c> ctor form: it must derive the factory target from its type argument
/// and flow through the identical <c>GenerateTemplate</c>/<c>Emit</c> shape, so a top-level generic template is
/// byte-identical to the same template written with the <c>typeof</c> ctor. Also proves carrier/child recognition
/// (<see cref="ChildTemplateFinder"/>/<c>TemplateScope</c>) recognizes a generic carrier and folds its nested bare
/// <c>[Template]</c> sub-template (a nested <c>[Template&lt;T&gt;]</c> would be SY1034).
/// </summary>
public class GenericTemplateAttributeTest
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
    ];

    private static CSharpCompilation CompilationWithSource(string source)
    {
        MetadataReference[] references = [.. References, MetadataReference.CreateFromFile(SyntoCoreAssembly.Location)];
        return CSharpCompilation.Create("GenericTemplateAttributeTest",
            [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Dictionary<string, string> RunGenerator(string source)
    {
        var compilation = CompilationWithSource(source);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var runResult = result.GetRunResult();
        Assert.Empty(runResult.Diagnostics);
        return runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);
    }

    private const string GenericForm =
        """
        using System;
        using Synto.Templating;

        partial class Factory {}

        public class TestClass
        {
            [Template<Factory>]
            static void Sample()
            {
                Console.WriteLine("Hello world");
            }
        }
        """;

    private const string GenericBareForm =
        """
        using System;
        using Synto.Templating;

        partial class Factory {}

        public class TestClass
        {
            [Template<Factory>(Options = TemplateOption.Bare)]
            static void Sample()
            {
                Console.WriteLine("Hello world");
            }
        }
        """;

    // Generic-CARRIER clone of ChildTemplateTest.NestedChildSpliceValueComposition: a [Template<Factory>] carrier
    // with a nested bare [Template] sub-template. Proves ChildTemplateFinder/TemplateScope recognize the GENERIC
    // carrier (matched on the attribute's original definition) and fold the nested bare child into the parent's
    // single document, so the child's [Splice] value param never leaks into the parent factory. (A nested
    // [Template<T>] here would be illegal — SY1034 — so the child rides the bare sub-template surface.)
    private const string GenericNestedChildSpliceValueComposition =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

        partial class Factory {}

        public readonly record struct Col(int Ordinal, string Name, string ClrType);

        [Template<Factory>]
        public class Reader
        {
            [Template]
            public TRet TypedGetter<[Splice] TRet>(int i, [Splice] object childOnlySplice)
            {
                var columns = Unquote<IReadOnlyList<Col>>();
                var clrType = Unquote<string>();
                var typeLabel = Unquote<string>();
                foreach (var c in columns.Where(c => c.ClrType == clrType))
                    if (i == c.Ordinal)
                        return Member<TRet>(childOnlySplice, c.Name);
                throw new global::System.InvalidCastException($"Field {i} is not {typeLabel} column.");
            }

            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Getters()
            {
                var columns = Unquote<IReadOnlyList<Col>>();
                yield return Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, "System.Int32", "an Int32")
                    .WithIdentifier(Identifier("GetInt32"));
            }
        }
        """;

    [Fact]
    public void GenericForm_ProducesFactory()
    {
        // The ctor form [Template(typeof(T))] is gone (Phase 1 clean break), so the generic form is now the only
        // way to pin a target. It must still generate a factory document.
        var genericSources = RunGenerator(GenericForm);

        Assert.NotEmpty(genericSources);
    }

    [Fact]
    public void GenericForm_HonorsBareOption()
    {
        // Options = Bare changes the generated factory body, so the Bare form's output differs from the default.
        var defaultSources = RunGenerator(GenericForm);
        var bareSources = RunGenerator(GenericBareForm);

        Assert.NotEmpty(bareSources);
        Assert.NotEqual(defaultSources, bareSources);
    }

    [Fact]
    public void GenericNestedChild_SpliceValueParam_DoesNotLeakIntoParentFactory()
    {
        var sources = RunGenerator(GenericNestedChildSpliceValueComposition);

        Assert.Contains("Factory.Reader.g.cs", sources.Keys);

        // The nested bare [Template] folds into the parent's single document as an `internal static` sub-template,
        // so its own [Splice] param (childOnlySplice) legitimately lives in the CHILD region. Scope the leak guard
        // to the PARENT factory method region to prove it does not leak into the parent factory's signature.
        var parentFactory = ParentFactoryRegion(sources["Factory.Reader.g.cs"]);
        Assert.DoesNotContain("childOnlySplice", parentFactory, StringComparison.Ordinal);
    }

    // Returns the text of the public parent factory method (from `public static` up to the first `internal static`
    // sub-template that follows it) so leak assertions target the parent's signature without the folded-in child
    // sub-template body.
    private static string ParentFactoryRegion(string document)
    {
        var start = document.IndexOf("public static", StringComparison.Ordinal);
        if (start < 0)
            return document;

        var subTemplateStart = document.IndexOf("internal static", start, StringComparison.Ordinal);
        return subTemplateStart < 0 ? document.Substring(start) : document.Substring(start, subTemplateStart - start);
    }
}
